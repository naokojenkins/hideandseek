using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.RL;
using HideAndSeek.Core.RaylibThreeD;
using HideAndSeek.Core.Rendering;
using TorchSharp;

namespace ToolUse.Sim.Application
{
    /// <summary>
    /// Thin orchestrator for composing config, agents, world, simulation and optional renderer.
    /// Keeps Program.cs minimal.
    /// </summary>
    public class SimulationApp
    {
        private readonly int _screenW;
        private readonly int _screenH;
        private readonly int _fps;
        private readonly IWindowRenderer? _renderer;
        private readonly bool _useVisualization;
        private readonly System.IServiceProvider? _services;
        private readonly ILogger<SimulationApp> _log;

        private readonly string ModelDir;
        private readonly string SeekerModelPath;
        private readonly string HiderModelPath;
        private readonly string SeekerStatePath;
        private readonly string HiderStatePath;

        private bool _evalMode = false;

        private GameConfig _config = null!;
        private int _gridSize;
        private Simulation3D _simulation = null!;
        private DQNAgent _seekerDqn = null!;
        private DQNAgent _hiderDqn = null!;
        private DateTime _lastConsoleUpdate = DateTime.Now;
        private bool _episodeOverlayPendingSave = false;
        private DateTime _lastAutosaveUtc = DateTime.UtcNow;
        private readonly object _saveLock = new object();
        private bool _isShuttingDown = false;
        private float _timeScaleAccum = 0f;

        public SimulationApp(bool useVisualization, IWindowRenderer? renderer = null, int screenW = 1024, int screenH = 768, int fps = 60, System.IServiceProvider? services = null)
        {
            _useVisualization = useVisualization;
            _renderer = renderer;
            _screenW = screenW;
            _screenH = screenH;
            _fps = fps;
            _services = services;
            _log = (services?.GetService(typeof(ILogger<SimulationApp>)) as ILogger<SimulationApp>) ?? NullLogger<SimulationApp>.Instance;

            ModelDir = HideAndSeek.Core.IO.PathService.GetModelsDirectory();
            SeekerModelPath = Path.Combine(ModelDir, "seeker.pt");
            HiderModelPath  = Path.Combine(ModelDir, "hider.pt");
            SeekerStatePath = Path.Combine(ModelDir, "seeker_state.json");
            HiderStatePath  = Path.Combine(ModelDir, "hider_state.json");
        }

        public void Run(CancellationToken cancellationToken = default)
        {
            _config = GameConfig.Instance;

            if (_config.Seed != 0)
            {
                try { TorchSharp.torch.random.manual_seed(_config.Seed); } catch { }
                try { if (TorchSharp.torch.cuda.is_available()) TorchSharp.torch.cuda.manual_seed_all(_config.Seed); } catch { }
            }

            _gridSize = _config.World.GridSize;

            // Init minimal world to deduce state/action sizes
            int actionSize = Math.Max(1, _config.Actions.Count);
            var world = new World3D(_gridSize);
            world.GenerateStaticGrid();
            var dummySeeker = new Agent3D(new Vector3(0, 0, 0), true);
            var dummyHider  = new Agent3D(new Vector3(0, 0, 0), false);
            var adapter = new SimAdapter3D(world, dummySeeker, dummyHider);
            var dummyState = adapter.GetSeekerState();
            int stateSize = dummyState.ToArray(_gridSize).Length;

            if (_services != null)
            {
                var deviceProvider = (HideAndSeek.Core.RL.IDeviceProvider?)_services.GetService(typeof(HideAndSeek.Core.RL.IDeviceProvider));
                var optimizerFactory = (HideAndSeek.Core.RL.IOptimizerFactory?)_services.GetService(typeof(HideAndSeek.Core.RL.IOptimizerFactory));
                var rbFactory = (HideAndSeek.Core.RL.IReplayBufferFactory?)_services.GetService(typeof(HideAndSeek.Core.RL.IReplayBufferFactory));
                var effective = _config.BuildEffectiveDqnConfig();
                _seekerDqn = new DQNAgent(stateSize, actionSize, effective, null, deviceProvider, optimizerFactory, rbFactory);
                _hiderDqn  = new DQNAgent(stateSize, actionSize, effective, null, deviceProvider, optimizerFactory, rbFactory);
            }
            else
            {
                var effective = _config.BuildEffectiveDqnConfig();
                _seekerDqn = new DQNAgent(stateSize, actionSize, effective);
                _hiderDqn  = new DQNAgent(stateSize, actionSize, effective);
            }
            _hiderDqn.SetForceExploitWhenSeen(_config.Hider.ForceExploitWhenSeen);
            _seekerDqn.SetForceExploitWhenSeen(_config.Seeker.ForceExploitWhenSeen);

            // Emit an immediate initial training snapshot so dashboard has data instantly
            try
            {
                var mr = HideAndSeek.Core.IO.MetricsRecorder.Instance;
                // Use current epsilons from agents and buffer counts (0 at start), emaLoss=0
                float eps = _seekerDqn != null ? (float)typeof(HideAndSeek.Core.RL.DQNAgent).GetField("epsilon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_seekerDqn)! : 0f;
                // If reflection fails, fallback to config
                if (eps <= 0f) eps = _config.DQN.EpsilonStart;
                float beta = _config.ReplayBuffer.BetaStart;
                int buf = 0;
                mr.RecordTraining(0, eps, beta, buf, 0f, 0f, 0f);
            }
            catch { }

            // Try loading latest checkpoint (configurable); fall back to legacy single files
            bool tryResume = _config?.Training?.ResumeFromLatest ?? true;
            if (tryResume)
            {
                if (!HideAndSeek.Core.IO.CheckpointManager.LoadLatest(_seekerDqn, _hiderDqn))
                {
                    _seekerDqn.LoadAll(SeekerModelPath, SeekerStatePath);
                    _hiderDqn.LoadAll(HiderModelPath, HiderStatePath);
                }
            }
            else
            {
                _seekerDqn.LoadAll(SeekerModelPath, SeekerStatePath);
                _hiderDqn.LoadAll(HiderModelPath, HiderStatePath);
            }

            if (_evalMode)
            {
                try
                {
                    _seekerDqn.SetEpsilon(_config.DQN.EpsilonMin);
                    _hiderDqn.SetEpsilon(_config.DQN.EpsilonMin);
                }
                catch { }
            }

            ResetSession();

            if (_useVisualization && _renderer != null)
            {
                _renderer.InitWindow(_screenW, _screenH, "3D Hide & Seek (DQN)");
                _renderer.SetTargetFps(_fps);
            }

            float stepDt = 1f / _fps;
            // Apply TimeScale by scaling delta time directly; keeps physics stable and affects both modes uniformly
            float timeScaleSpeed = MathF.Max(0.0001f, _config.TimeScale);

            var targetFrameTime = TimeSpan.FromSeconds(stepDt);
            var frameTimer = System.Diagnostics.Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                frameTimer.Restart();
                float effectiveDt = stepDt * timeScaleSpeed;

                if (_useVisualization && _renderer != null)
                {
                    if (_renderer.WindowShouldClose()) break;

                    _simulation?.HandleInput();
                    if (cancellationToken.IsCancellationRequested) break;
                    UpdateDqnContexts();
                    _simulation?.Update(effectiveDt);

                    _renderer.BeginDrawing();
                    _renderer.ClearBackground(ColorRgba.LightGray);
                    _simulation?.Draw();

                    if (_episodeOverlayPendingSave)
                    {
                        int fontSize = 48;
                        int textW = _renderer.MeasureText("Episode over - creating new one", fontSize);
                        int x = (_screenW - textW) / 2;
                        int y = (_screenH - fontSize) / 2;
                        _renderer.DrawText("Episode over - creating new one", x + 2, y + 2, fontSize, new ColorRgba(0, 0, 0, 180));
                        _renderer.DrawText("Episode over - creating new one", x, y, fontSize, ColorRgba.White);
                    }

                    _renderer.EndDrawing();

                    if (_episodeOverlayPendingSave)
                    {
                        TrySaveModels();
                        _episodeOverlayPendingSave = false;
                    }
                }
                else
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    UpdateDqnContexts();
                    _simulation?.Update(effectiveDt);

                    if ((DateTime.Now - _lastConsoleUpdate).TotalSeconds >= 1)
                    {
                        PrintConsoleHUD();
                        _lastConsoleUpdate = DateTime.Now;
                    }

                    // Periodic autosave by wall-clock time
                    int autosaveSec = Math.Max(0, _config?.Training?.AutosaveSeconds ?? 0);
                    if (autosaveSec > 0 && (DateTime.UtcNow - _lastAutosaveUtc).TotalSeconds >= autosaveSec)
                    {
                        TrySaveModels();
                        _lastAutosaveUtc = DateTime.UtcNow;
                    }
                }

                // Frame pacing: in headless mode we explicitly sleep to cap CPU; when using renderer, Raylib caps FPS via SetTargetFps.
                if (!_useVisualization || _renderer == null)
                {
                    var elapsed = frameTimer.Elapsed;
                    if (elapsed < targetFrameTime)
                    {
                        int sleepMs = (int)Math.Max(0, (targetFrameTime - elapsed).TotalMilliseconds);
                        if (sleepMs > 0)
                        {
                            try { System.Threading.Thread.Sleep(sleepMs); } catch { }
                        }
                    }
                }
            }

            Shutdown();
        }

        private void UpdateDqnContexts()
        {
            if (_simulation == null) return;
            // Use already computed visibility from Simulation3D to avoid expensive recomputation each tick.
            bool isHiderSeen = _simulation.IsHiderVisible;
            _hiderDqn?.SetExternalContext(new ExternalContext { IsHider = true, IsHiderSeen = isHiderSeen });
            _seekerDqn?.SetExternalContext(new ExternalContext { IsHider = false, IsHiderSeen = isHiderSeen });
        }

        private void ResetSession()
        {
            var world = new World3D(_gridSize);
            world.GenerateStaticGrid();

            float seekerRadius = _config.Seeker.AgentRadius;
            float hiderRadius  = _config.Hider.AgentRadius;

            int seekerCount = Math.Max(1, _config.Seeker.Count);
            int hiderCount  = Math.Max(1, _config.Hider.Count);

            var seekers = new List<Agent3D>(seekerCount);
            var hiders  = new List<Agent3D>(hiderCount);

            static bool TooCloseXZ(Vector3 a, Vector3 b, float minDist)
            {
                float dx = a.X - b.X;
                float dz = a.Z - b.Z;
                return (dx * dx + dz * dz) < (minDist * minDist);
            }

            float sameTeamMinSeparationS = MathF.Max(2f * seekerRadius, 0.6f);
            float sameTeamMinSeparationH = MathF.Max(2f * hiderRadius, 0.6f);
            float crossTeamMinSeparation = MathF.Max(_config.MinInitialSeparation, 1.0f);

            var rand = new System.Random();
            for (int i = 0; i < seekerCount; i++)
            {
                Vector3 pos;
                int attempts = 0;
                do
                {
                    pos = world.GetRandomValidAgentPosition(seekerRadius, 0f);
                    attempts++;
                    if (attempts > 200) break;
                }
                while (seekers.Exists(s => TooCloseXZ(s.Position, pos, sameTeamMinSeparationS)));
                seekers.Add(new Agent3D(pos, true, rand.Next(0, 359)));
            }

            for (int i = 0; i < hiderCount; i++)
            {
                Vector3 pos;
                int attempts = 0;
                do
                {
                    pos = world.GetRandomValidAgentPosition(hiderRadius, 0f);
                    attempts++;
                    if (attempts > 200) break;
                }
                while (hiders.Exists(h => TooCloseXZ(h.Position, pos, sameTeamMinSeparationH)) ||
                       seekers.Exists(s => TooCloseXZ(s.Position, pos, crossTeamMinSeparation)));
                hiders.Add(new Agent3D(pos, false, rand.Next(0, 359)));
            }

            var newSeeker = seekers[0];
            var newHider = hiders[0];

            if (_simulation == null)
            {
                _simulation = new Simulation3D(_gridSize, newSeeker, newHider, _seekerDqn, _hiderDqn);
            }
            else
            {
                _simulation.Reset(newSeeker, newHider);
            }

            _simulation.SetAgents(seekers, hiders);

            _simulation.OnSessionCompleted += () =>
            {
                if (_useVisualization)
                {
                    _episodeOverlayPendingSave = true;
                }
                else
                {
                    TrySaveModels();
                }
            };
        }

        private void TrySaveModels()
        {
            lock (_saveLock)
            {
                try
                {
                    int keepLast = Math.Max(1, _config?.Training?.CheckpointKeepLast ?? 5);
                    HideAndSeek.Core.IO.CheckpointManager.SaveAgents(_seekerDqn, _hiderDqn, meta: new { mode = _evalMode ? "eval" : "train" }, keepLast: keepLast);
                    Simulation3D.ForceSaveTotalSessions();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Save failed: {Message}", ex.Message);
                }
            }
        }

        private void PrintConsoleHUD()
        {
            // Periodic structured metrics log to avoid silent failures in headless mode
            _log.LogInformation(
                "HUD session={Session} totalSessions={Total} time={Time:F1}/{Duration:F0} seekerPos={SeekerPos} hiderPos={HiderPos} seekerSeesHider={SeekerSees} hiderSeesSeeker={HiderSees} exploredSeeker={ExplS} exploredHider={ExplH}",
                _simulation.Session,
                Simulation3D.TotalSessions,
                _simulation.Timer,
                _simulation.SessionDurationSeconds,
                _simulation.Seeker.Position,
                _simulation.Hider.Position,
                _simulation.Seeker.CanSee(_simulation.Hider, _simulation.World),
                _simulation.Hider.CanSee(_simulation.Seeker, _simulation.World),
                _simulation.Seeker.GetTotalExploredCount(),
                _simulation.Hider.GetTotalExploredCount());
        }

        public void Shutdown()
        {
            lock (_saveLock)
            {
                if (_isShuttingDown) return;
                _isShuttingDown = true;
            }

            // best-effort final save
            try { TrySaveModels(); } catch { /* ignore */ }

            try
            {
                if (_useVisualization && _renderer != null && _renderer.IsWindowReady())
                {
                    _renderer.CloseWindow();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Renderer close error: {Message}", ex.Message);
            }
            _log.LogInformation("Program finished. Total sessions in history: {TotalSessions}", Simulation3D.TotalSessions);
        }

        public void SetEvalMode(bool enabled)
        {
            _evalMode = enabled;
            if (_simulation != null)
            {
                _simulation.EnableLearning = !enabled;
            }
        }
    }
}
