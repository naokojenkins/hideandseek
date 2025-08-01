using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core.RL;
using ToolUse.Core.Config;
using ToolUse.Core.RaylibThreeD;

namespace ToolUse.Core.RaylibThreeD
{
    public class Simulation3D
    {
        public World3D World { get; }
        public Agent3D Seeker { get; set; }
        public Agent3D Hider { get; set; }
        public bool IsHiderCaught => _isHiderCaught;

        private float sessionDurationSeconds;
        public void SetSessionDuration(float seconds) => sessionDurationSeconds = seconds;

        public GameConfig Config { get; private set; }
        public event Action? OnSessionCompleted;

        private DQNAgent _seekerAgent;
        private DQNAgent _hiderAgent;
        private SimAdapter3D _adapter;

        private Camera3D _camera;
        private Camera3D _fixedCameraState;
        private bool _followAgent = false;
        private bool _showVisionCones = true;
        private bool _showGrid = true;

        public int Session { get; private set; } = 1;
        public static int TotalSessions { get; private set; } = 0;
        public float Timer { get; private set; } = 0f;
        public float SeekerScore { get; private set; } = 0f;
        public float HiderScore { get; private set; } = 0f;
        public float ExplorationScore { get; private set; } = 0f;

        private bool _isHiderVisible = false;
        private float _lastVisibilityCheck = 0f;
        private const float _visibilityCheckInterval = 0.05f;

        private static readonly string SessionCounterFile = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "qtables",
            "total_sessions.json");

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        public bool IsHiderVisible
        {
            get => _isHiderVisible;
            private set => _isHiderVisible = value;
        }

        private bool _isHiderCaught = false;
        private int _caughtFrames = 0;

        private State _prevSeekerState;
        private State _prevHiderState;
        private long _prevSeekerAction;
        private long _prevHiderAction;

        private int _prevPhysicalExplored = 0;
        private int _prevVisualExplored = 0;
        private bool _catchBonusGiven = false;
        private bool _wasHiderVisiblePrev = false;

        static Simulation3D()
        {
            LoadTotalSessions();
        }

// ... остальные поля и код

        public Simulation3D(
            int worldSize,
            Agent3D seeker,
            Agent3D hider,
            DQNAgent seekerAgent,
            DQNAgent hiderAgent,
            string configPath = "game_config.json")
        {
            Config = GameConfig.Load(configPath);
            sessionDurationSeconds = Config.SessionDurationSeconds;

            World = new World3D(worldSize);
            World.GenerateStaticGrid();

            Seeker = seeker;
            Seeker.InitWorldSize(World.Size);

            Hider = hider;
            Hider.InitWorldSize(World.Size);

            _adapter = new SimAdapter3D(World, Seeker, Hider);
            _seekerAgent = seekerAgent;
            _hiderAgent  = hiderAgent;

            InitializeCamera();

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = Seeker.CanSee(Hider, World);
        }


        private void InitializeCamera()
        {
            _camera = new Camera3D
            {
                Position = new Vector3(World.Size / 2f, 25f, World.Size / 2f + 0.01f),
                Target = new Vector3(World.Size / 2f, 0f, World.Size / 2f),
                Up = Vector3.UnitY,
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };
            _fixedCameraState = _camera;
        }

        private void UpdateCamera()
        {
            if (!_followAgent)
            {
                Raylib.UpdateCamera(ref _camera, CameraMode.Free);
                _fixedCameraState = _camera;
            }
            else
            {
                _camera = _fixedCameraState;
            }
        }

        public void HandleInput()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F))
            {
                _followAgent = !_followAgent;
                if (_followAgent)
                {
                    _fixedCameraState = _camera;
                    Raylib.EnableCursor();
                }
                else
                {
                    _camera = _fixedCameraState;
                    Raylib.DisableCursor();
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.V)) _showVisionCones = !_showVisionCones;
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _showGrid = !_showGrid;
            if (Raylib.IsKeyPressed(KeyboardKey.R)) Restart();
        }

        public void Update(float deltaTime)
        {
            Timer += deltaTime;
            UpdateRLAgents(deltaTime);
            UpdateCamera();

            _lastVisibilityCheck += deltaTime;
            if (_lastVisibilityCheck >= _visibilityCheckInterval)
            {
                IsHiderVisible = Seeker.CanSee(Hider, World);
                _lastVisibilityCheck = 0f;
            }

            if (IsHiderVisible)
            {
                if (++_caughtFrames >= Config.FramesForCatch)
                {
                    _isHiderCaught = true;
                }
            }
            else
            {
                _caughtFrames = 0;
            }

            UpdateScores(deltaTime);

            if (_isHiderCaught || Timer > Config.SessionDurationSeconds)
            {
                Restart();
            }
        }

        private void UpdateRLAgents(float deltaTime)
        {
            var seekerState = _adapter.GetSeekerState();
            var hiderState  = _adapter.GetHiderState();

            var seekerAction = _seekerAgent.ChooseAction(seekerState.ToArray());
            var hiderAction  = _hiderAgent.ChooseAction(hiderState.ToArray());

            int beforePhysical  = Seeker.GetExploredCount();
            int beforeVisual    = Seeker.GetVisuallyExploredCount();

            _adapter.ApplyAction(Seeker, seekerAction);
            _adapter.ApplyAction(Hider, hiderAction);

            if (seekerAction == 2) Seeker.MoveWithCollisionAvoidance(World, deltaTime, Hider);
            if (hiderAction == 2) Hider.MoveWithCollisionAvoidance(World, deltaTime, Seeker);

            Seeker.UpdateVisualExploration(World);
            Hider.UpdateVisualExploration(World);

            int afterPhysical   = Seeker.GetExploredCount();
            int afterVisual     = Seeker.GetVisuallyExploredCount();

            int newPhysical = afterPhysical - beforePhysical;
            int newVisual   = afterVisual   - beforeVisual;
            if (newPhysical < 0) newPhysical = 0;
            if (newVisual   < 0) newVisual   = 0;

            if (_prevSeekerState != null)
            {
                float seekerReward = ComputeSeekerReward();
                float expPhysBonus   = newPhysical * Config.Seeker.PhysicalExploreReward;
                float expVisualBonus = newVisual   * Config.Seeker.VisualExploreReward;
                seekerReward += expPhysBonus + expVisualBonus;
                ExplorationScore += expPhysBonus + expVisualBonus;

                if (_isHiderCaught && !_catchBonusGiven)
                {
                    seekerReward += Config.Seeker.CatchBonus;
                    _catchBonusGiven = true;
                }

                _seekerAgent.Store(_prevSeekerState.ToArray(), _prevSeekerAction, seekerReward, seekerState.ToArray(), _isHiderCaught);
                _seekerAgent.Learn();
            }
            if (_prevHiderState != null)
            {
                float hiderReward = ComputeHiderReward();
                if (_wasHiderVisiblePrev && !IsHiderVisible)
                {
                    hiderReward += Config.Hider.EscapeBonus;
                }
                _hiderAgent.Store(_prevHiderState.ToArray(), _prevHiderAction, hiderReward, hiderState.ToArray(), _isHiderCaught);
                _hiderAgent.Learn();
            }

            _prevSeekerState = seekerState;
            _prevHiderState  = hiderState;
            _prevSeekerAction = seekerAction;
            _prevHiderAction  = hiderAction;
            _wasHiderVisiblePrev = IsHiderVisible;
        }

        private float ComputeSeekerReward()
        {
            return IsHiderVisible ? Config.Seeker.RewardWhenHiderVisible : Config.Seeker.RewardWhenHiderHidden;
        }
        private float ComputeHiderReward()
        {
            return IsHiderVisible ? Config.Hider.RewardWhenVisible : Config.Hider.RewardWhenHidden;
        }

        private void UpdateScores(float deltaTime)
        {
            if (IsHiderVisible)
            {
                SeekerScore += Config.Seeker.PointsPerSecondWhenHiderVisible * deltaTime;
                HiderScore  += Config.Hider.PointsPerSecondWhenVisible * deltaTime;
            }
            else
            {
                HiderScore  += Config.Hider.PointsPerSecondWhenHidden * deltaTime;
                SeekerScore += Config.Seeker.PointsPerSecondWhenHiderHidden * deltaTime;
            }
        }

        public void Restart()
        {
            Session++;
            TotalSessions++;

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = false;

            World.GenerateStaticGrid();

            Vector3 seekerPos = World.GetRandomEmptyPosition(0f);
            Seeker.Position = seekerPos;
            Seeker.Direction = Raylib.GetRandomValue(0, 359);
            Seeker.InitWorldSize(World.Size);

            Vector3 hiderPosition = World.GetRandomEmptyPosition(0f);
            int attempts = 0;
            while (attempts < 50 && Vector3.Distance(seekerPos, hiderPosition) < 15f)
            {
                hiderPosition = World.GetRandomEmptyPosition(0f);
                attempts++;
            }
            Hider.Position = hiderPosition;
            Hider.Direction = Raylib.GetRandomValue(0, 359);
            Hider.InitWorldSize(World.Size);

            Seeker.ResetExploration();

            _prevSeekerState = null;
            _prevHiderState = null;
            _prevSeekerAction = 0;
            _prevHiderAction = 0;

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();
        }

        public void Reset(Agent3D newSeeker, Agent3D newHider)
        {
            Seeker = newSeeker;
            Hider = newHider;

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = false;

            Seeker.InitWorldSize(World.Size);
            Hider.InitWorldSize(World.Size);

            Seeker.ResetExploration();

            _prevSeekerState = null;
            _prevHiderState = null;
            _prevSeekerAction = 0;
            _prevHiderAction = 0;

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();
        }

        public void Draw()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(245, 245, 245, 255));
            Raylib.BeginMode3D(_camera);
            {
                World.Draw(true);
                if (_showGrid) World.DrawGrid();
                Seeker.Draw();
                Hider.Draw();
                if (_showVisionCones)
                {
                    Color seekerConeColor = IsHiderVisible ? new Color(255, 255, 0, 100) : new Color(0, 0, 255, 80);
                    Seeker.DrawVisionCone(World, seekerConeColor);
                    Hider.DrawVisionCone(World, new Color(0, 255, 0, 80));
                }
            }
            Raylib.EndMode3D();

            DrawHUD();
        }

        private void DrawHUD()
        {
            Raylib.DrawRectangle(5, 5, 340, 290, new Color(0, 0, 0, 180));
            int y = 10;
            Raylib.DrawText($"Session: {Session} / Total: {TotalSessions}", 10, y, 20, Color.White); y += 25;
            Color timeColor = Timer > (Config.SessionDurationSeconds * 0.9f) ? Color.Red : Color.White;
            Raylib.DrawText($"Time: {Timer:F1}s / {Config.SessionDurationSeconds:F0}s", 10, y, 20, timeColor); y += 25;

            Raylib.DrawText($"Seeker: {SeekerScore:F1}", 10, y, 20, new Color(60, 120, 255, 255));
            float seekerPercent = SeekerScore / Config.SessionDurationSeconds * 100f;
            Raylib.DrawRectangle(180, y + 5, (int)(100 * Math.Min(seekerPercent / 100f, 1.0f)), 10, new Color(60, 120, 255, 255));
            Raylib.DrawRectangleLines(180, y + 5, 130, 10, Color.White); y += 25;

            Raylib.DrawText($"Physical: {Seeker.GetExploredCount()}", 10, y, 18, new Color(255, 220, 0, 255)); y += 22;
            Raylib.DrawText($"Visual: {Seeker.GetVisuallyExploredCount()}", 10, y, 18, new Color(255, 170, 30, 255)); y += 22;
            Raylib.DrawText($"Total: {Seeker.GetTotalExploredCount()}", 10, y, 18, new Color(150, 80, 255, 255)); y += 22;
            Raylib.DrawText($"Seeker Known Walls: {Seeker.KnownWalls.Count}", 10, y, 18, new Color(80, 180, 255, 255)); y += 22;
            Raylib.DrawText($"Exploration Score: {ExplorationScore:F1}", 10, y, 18, new Color(120, 70, 255, 255)); y += 25;
            Raylib.DrawText($"Hider: {HiderScore:F1}", 10, y, 20, new Color(40, 200, 60, 255));
            float hiderPercent = HiderScore / Config.SessionDurationSeconds * 100f;
            Raylib.DrawRectangle(180, y + 5, (int)(100 * Math.Min(hiderPercent / 100f, 1.0f)), 10, new Color(40, 200, 60, 255));
            Raylib.DrawRectangleLines(180, y + 5, 130, 10, Color.White); y += 25;
            Raylib.DrawText($"Hider Known Walls: {Hider.KnownWalls.Count}", 10, y, 18, new Color(80, 255, 120, 255)); y += 22;
            float distance = Vector3.Distance(Seeker.Position, Hider.Position);
            Raylib.DrawText($"Distance: {distance:F1}", 10, y, 18, new Color(120, 120, 120, 255)); y += 22;
            string visibilityText = IsHiderVisible ? "VISIBLE" : "HIDDEN";
            Color visibilityColor = IsHiderVisible ? Color.Red : new Color(0, 200, 60, 255);
            Raylib.DrawText($"Hider: {visibilityText}", 10, y, 18, visibilityColor); y += 22;
            if (_isHiderCaught)
                Raylib.DrawText("CAUGHT!", 10, y, 24, Color.Red);
        }

        private static void LoadTotalSessions()
        {
            try
            {
                string directory = Path.GetDirectoryName(SessionCounterFile);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(SessionCounterFile))
                {
                    TotalSessions = 0;
                    return;
                }
                string json = File.ReadAllText(SessionCounterFile);
                var data = JsonConvert.DeserializeObject<SessionCounterData>(json, JsonSettings);
                TotalSessions = data?.TotalSessions ?? 0;
            }
            catch { TotalSessions = 0; }
        }

        private static void SaveTotalSessions()
        {
            try
            {
                var data = new SessionCounterData
                {
                    TotalSessions = TotalSessions,
                    LastUpdate = DateTime.Now
                };
                string json = JsonConvert.SerializeObject(data, Formatting.Indented, JsonSettings);
                File.WriteAllText(SessionCounterFile, json);
            }
            catch { }
        }

        public static void ForceSaveTotalSessions() => SaveTotalSessions();

        private class SessionCounterData
        {
            public int TotalSessions { get; set; }
            public DateTime LastUpdate { get; set; }
        }
    }
}
