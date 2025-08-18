using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core.RL;
                    using ToolUse.Core.Config;
using ToolUse.Core.RaylibThreeD;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace ToolUse.Core.RaylibThreeD
{
    public partial class Simulation3D
    {
        private float _lastHiderDistance = 0f;
        public World3D World { get; }
        public Agent3D Seeker { get; set; }
        public Agent3D Hider { get; set; }

        // Новые коллекции: все агенты по ролям
        public List<Agent3D> Seekers { get; private set; } = new();
        public List<Agent3D> Hiders { get; private set; } = new();

        public bool IsHiderCaught => _isHiderCaught;

        private float sessionDurationSeconds;
        public void SetSessionDuration(float seconds) => sessionDurationSeconds = seconds;
        public float SessionDurationSeconds => sessionDurationSeconds;

        public GameConfig Config { get; private set; }
        public event Action? OnSessionCompleted;

        private DQNAgent _seekerAgent;
        private DQNAgent _hiderAgent;

        // Командные blackboard'ы
        private readonly TeamBlackboard _seekersBoard = new();
        private readonly TeamBlackboard _hidersBoard  = new();

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
        private float _visibilityCheckInterval = 0.05f;

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

        private int _prevPhysicalExplored = 0;
        private int _prevVisualExplored = 0;
        private bool _catchBonusGiven = false;
        private bool _wasHiderVisiblePrev = false;

        // Флаг, чтобы выходить из Update в кадре, где произошел Restart
        private bool _justRestarted = false;

        static Simulation3D()
        {
            LoadTotalSessions();
        }

        private void CheckNaN(float[] arr, string tag)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (float.IsNaN(arr[i]) || float.IsInfinity(arr[i]))
                {
                    try
                    {
                        LogNumericIssue(tag, $"Array<float> length={arr.Length}, badIndex={i}, value={arr[i]}");
                    }
                    catch { }
                    throw new Exception($"[NaN/Inf] {tag}: Index {i} value {arr[i]}");
                }
            }
        }
        private void CheckNaN(Vector3 v, string tag)
        {
            if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
                float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z))
            {
                try
                {
                    LogNumericIssue(tag, $"Vector3 value=({v.X}, {v.Y}, {v.Z})");
                }
                catch { }
                throw new Exception($"[NaN/Inf] {tag}: {v}");
            }
        }

        // Вспомогательная проверка без броска исключения
        private static bool IsFiniteVec(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
                     float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        // Санитизация сцены перед отрисовкой: камера и позиции агентов
        private void SanitizeScene()
        {
            // Камера
            if (!IsFiniteVec(_camera.Position) || !IsFiniteVec(_camera.Target) || !IsFiniteVec(_camera.Up) ||
                float.IsNaN(_camera.FovY) || float.IsInfinity(_camera.FovY))
            {
                InitializeCamera();
            }

            // Агенты: используем актуальные списки, если они заданы
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            void FixAgent(Agent3D a)
            {
                var p = a.Position;
                bool badPos = float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
                              float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z);
                if (badPos || !IsPositionValidForWorld(p, a.AgentRadius))
                {
                    a.Position = World.GetRandomValidAgentPosition(a.AgentRadius, 0f);
                }
                if (float.IsNaN(a.Direction) || float.IsInfinity(a.Direction))
                {
                    a.Direction = 0f;
                }
            }

            foreach (var s in seekers) FixAgent(s);
            foreach (var h in hiders)  FixAgent(h);
        }

        // Проверка валидности позиции агента относительно текущего мира Simulation3D.World
        private bool IsPositionValidForWorld(Vector3 pos, float radius)
        {
            if (World == null) return false;
            int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                float ang = 2 * MathF.PI * i / steps;
                float checkX = pos.X + MathF.Cos(ang) * radius * 0.9f;
                float checkZ = pos.Z + MathF.Sin(ang) * radius * 0.9f;

                int gx = Math.Clamp((int)MathF.Floor(checkX), 0, World.Size - 1);
                int gz = Math.Clamp((int)MathF.Floor(checkZ), 0, World.Size - 1);

                if (!World.IsInside(gx, gz) || World.IsBlocked(gx, gz))
                    return false;
            }
            return true;
        }

        // Гарантирует, что агент стоит на валидной позиции текущего мира; при необходимости переносит
        private void EnsureAgentOnValidCell(Agent3D agent)
        {
            if (!IsPositionValidForWorld(agent.Position, agent.AgentRadius))
            {
                agent.Position = World.GetRandomValidAgentPosition(agent.AgentRadius, 0f);
            }
        }

        public Simulation3D(
            int worldSize,
            Agent3D seeker,
            Agent3D hider,
            DQNAgent seekerAgent,
            DQNAgent hiderAgent,
            string? configPath = null)
        {
            // Теперь используем GameConfig.Instance (все параметры уже загружены)
            Config = GameConfig.Instance;
            sessionDurationSeconds = Config.SessionDurationSeconds;

            // Параметры из конфига (вместо хардкода)
            _visibilityCheckInterval = MathF.Max(0.001f, Config.VisibilityCheckInterval);
            _actionRepeat = Math.Max(1, Config.ActionRepeat);
            _noProgressDistanceEps = Math.Max(0f, Config.NoProgressDistanceEps);
            _noProgressSeconds = Math.Max(0f, Config.NoProgressSeconds);

            World = new World3D(worldSize);
            World.GenerateStaticGrid();

            Seeker = seeker;
            Seeker.InitWorldSize(World.Size);
            Seeker.SetWorld(World);

            Hider = hider;
            Hider.InitWorldSize(World.Size);
            Hider.SetWorld(World);

            // Убедимся, что стартовые позиции валидны в текущем мире симуляции
            EnsureAgentOnValidCell(Seeker);
            EnsureAgentOnValidCell(Hider);

            _seekerAgent = seekerAgent;
            _hiderAgent  = hiderAgent;

            // Привязываем командные blackboard'ы
            Seeker.TeamBoard = _seekersBoard;
            Hider.TeamBoard  = _hidersBoard;

            InitializeCamera();

            // Синхронизация начального состояния показа конусов с Agent3D
            Agent3D.ShowVisionCones = _showVisionCones;

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = Seeker.CanSee(Hider, World);

            CheckNaN(Seeker.Position, "Simulation3D.ctor:Seeker.Position");
            CheckNaN(Hider.Position, "Simulation3D.ctor:Hider.Position");
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

            if (Raylib.IsKeyPressed(KeyboardKey.V))
            {
                _showVisionCones = !_showVisionCones;
                Agent3D.ShowVisionCones = _showVisionCones;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _showGrid = !_showGrid;
            if (Raylib.IsKeyPressed(KeyboardKey.R)) Restart();
        }

        public void Update(float deltaTime)
        {
            // Масштабируем логическое время симуляции (с защитой от NaN/Inf/некорректных значений)
            float timeScale = (!float.IsFinite(Config.TimeScale) || Config.TimeScale <= 0f) ? 1.0f : Config.TimeScale;
            float dt = deltaTime * timeScale;

            Timer += dt;

            // Эффективный порог «кадров видимости» с учётом сжатия времени
            int framesThreshold = Math.Max(1, (int)MathF.Round(Config.FramesForCatch / timeScale));

            // Предсказание завершения эпизода в этом кадре
            bool willCatchThisStep = IsHiderVisible && (_caughtFrames + 1 >= framesThreshold);
            bool willTimeoutThisStep = (Timer >= sessionDurationSeconds);

            UpdateRLAgents(dt, willCatchThisStep, willTimeoutThisStep);

            if (_justRestarted)
            {
                _justRestarted = false;
                return;
            }

            UpdateCamera();

            _lastVisibilityCheck += dt;
            if (_lastVisibilityCheck >= _visibilityCheckInterval)
            {
                IsHiderVisible = AnyHiderVisible();
                _lastVisibilityCheck = 0f;
            }

            if (IsHiderVisible)
            {
                if (++_caughtFrames >= framesThreshold)
                {
                    _isHiderCaught = true;
                }
            }
            else
            {
                _caughtFrames = 0;
            }

            UpdateScores(dt);

            if (_isHiderCaught || Timer >= sessionDurationSeconds)
            {
                try { OnSessionCompleted?.Invoke(); } catch { }
                Restart();
                return;
            }
        }

        // Action repeat and progress tracking
        private int _actionRepeat = 2;

        // Metrics
        private int _framesInSession = 0;
        private int _visibleFrames = 0;
        private float _sumDistance = 0f;
        private float _accSeekerReward = 0f;
        private float _accHiderReward = 0f;

        // Early termination (no progress)
        private float _noProgressTimer = 0f;
        private float _lastDistanceForProgress = 0f;
        private int _lastSeekerVisualExploredForProgress = 0;
        private float _noProgressDistanceEps = 0.05f;
        private float _noProgressSeconds = 5f;

        // ---- Переменные для мультиагентного шага ----
        // Предыдущее состояние и действие для каждого агента
        private readonly Dictionary<Agent3D, State> _prevStateSeekers = new();
        private readonly Dictionary<Agent3D, State> _prevStateHiders  = new();
        private readonly Dictionary<Agent3D, long>  _prevActionSeekers = new();
        private readonly Dictionary<Agent3D, long>  _prevActionHiders  = new();

        // Action repeat для каждого агента
        private readonly Dictionary<Agent3D, int> _repeatLeftSeekers = new();
        private readonly Dictionary<Agent3D, int> _repeatLeftHiders  = new();
        private readonly Dictionary<Agent3D, long> _currentActionSeekers = new();
        private readonly Dictionary<Agent3D, long> _currentActionHiders  = new();

        // Для наград Hider: последняя дистанция до ближайшего Seeker и видимость на прошлом шаге
        private readonly Dictionary<Agent3D, float> _lastDistToNearestSeeker = new();
        private readonly Dictionary<Agent3D, bool>  _wasHiderVisiblePrevMap  = new();

        // Для наград Seeker: последняя дистанция до ближайшего Hider (для штрафа за отсутствие прогресса)
        private readonly Dictionary<Agent3D, float> _lastDistToNearestHider = new();

        // Для наград Seeker: предыдущие счётчики исследования (физическое/визуальное)
        private readonly Dictionary<Agent3D, (int phys, int vis)> _prevExploreCountsSeekers = new();

        private void UpdateRLAgents(float deltaTime, bool isTerminalByCatchThisStep, bool isTerminalByTimeoutThisStep)
        {
            // Списки активных агентов (если коллекции пусты — используем одиночные)
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            // Единая семантика действий из конфига
            var actCfg = Config.Actions;

            // 1) Состояния ДО действия (state_t)
            var seekerStatesBefore = new Dictionary<Agent3D, State>(seekers.Count);
            var hiderStatesBefore  = new Dictionary<Agent3D, State>(hiders.Count);

            foreach (var s in seekers)
            {
                var target = GetNearestOpponent(s, hiders);
                var ad = new SimAdapter3D(World, s, target);
                var st = ad.GetSeekerState();
                CheckNaN(st.ToArray(World.Size), "seekerState_before");
                seekerStatesBefore[s] = st;

                if (!_lastDistToNearestHider.ContainsKey(s))
                    _lastDistToNearestHider[s] = Vector3.Distance(s.Position, target.Position);

                if (!_prevExploreCountsSeekers.ContainsKey(s))
                    _prevExploreCountsSeekers[s] = (s.GetExploredCount(), s.GetVisuallyExploredCount());
            }
            foreach (var h in hiders)
            {
                var watcher = GetNearestOpponent(h, seekers);
                var ad = new SimAdapter3D(World, watcher, h);
                var st = ad.GetHiderState();
                CheckNaN(st.ToArray(World.Size), "hiderState_before");
                hiderStatesBefore[h] = st;

                if (!_lastDistToNearestSeeker.ContainsKey(h))
                    _lastDistToNearestSeeker[h] = Vector3.Distance(h.Position, watcher.Position);
                if (!_wasHiderVisiblePrevMap.ContainsKey(h))
                    _wasHiderVisiblePrevMap[h] = false;
            }

            // Терминальность текущего шага (поимка или тайм-аут)
            bool isTerminalThisStep = isTerminalByCatchThisStep || isTerminalByTimeoutThisStep;

            // Подготовка catch-бонуса на кадр: поровну на всех Seeker (в кадре поимки)
            bool giveCatchBonus = isTerminalByCatchThisStep && !_catchBonusGiven;
            float perSeekerCatchBonus = giveCatchBonus ? (Config.Seeker.CatchBonus / Math.Max(1, seekers.Count)) : 0f;

            // 2) Выбор действия (action_t) с учётом action repeat
            foreach (var s in seekers)
            {
                if (!_repeatLeftSeekers.TryGetValue(s, out int left) || left <= 0)
                {
                    long a = _seekerAgent.ChooseAction(seekerStatesBefore[s].ToArray(World.Size));
                    _currentActionSeekers[s] = a;
                    _repeatLeftSeekers[s] = _actionRepeat - 1;
                }
                else
                {
                    _repeatLeftSeekers[s] = left - 1;
                }

                // Зафиксируем пары (state_t, action_t) для текущего кадра
                _prevStateSeekers[s] = seekerStatesBefore[s];
                _prevActionSeekers[s] = _currentActionSeekers[s];
            }
            foreach (var h in hiders)
            {
                if (!_repeatLeftHiders.TryGetValue(h, out int left) || left <= 0)
                {
                    long a = _hiderAgent.ChooseAction(hiderStatesBefore[h].ToArray(World.Size));
                    _currentActionHiders[h] = a;
                    _repeatLeftHiders[h] = _actionRepeat - 1;
                }
                else
                {
                    _repeatLeftHiders[h] = left - 1;
                }

                _prevStateHiders[h] = hiderStatesBefore[h];
                _prevActionHiders[h] = _currentActionHiders[h];
            }

            // 3) Применяем повороты (часть действия)
            foreach (var s in seekers)
            {
                float rot = Config.Seeker.RotationStepDegrees;
                long aNow = _currentActionSeekers.TryGetValue(s, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.TurnLeft || aNow == actCfg.ForwardLeft) s.Rotate(-rot);
                if (aNow == actCfg.TurnRight || aNow == actCfg.ForwardRight) s.Rotate(+rot);
            }
            foreach (var h in hiders)
            {
                float rot = Config.Hider.RotationStepDegrees;
                long aNow = _currentActionHiders.TryGetValue(h, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.TurnLeft || aNow == actCfg.ForwardLeft) h.Rotate(-rot);
                if (aNow == actCfg.TurnRight || aNow == actCfg.ForwardRight) h.Rotate(+rot);
            }

            // 4) Движение вперёд (часть действия) с учётом соседей
            foreach (var s in seekers)
            {
                long aNow = _currentActionSeekers.TryGetValue(s, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.Forward || aNow == actCfg.ForwardLeft || aNow == actCfg.ForwardRight)
                {
                    var neighbors = new List<Agent3D>();
                    foreach (var s2 in seekers) if (!ReferenceEquals(s2, s)) neighbors.Add(s2);
                    neighbors.AddRange(hiders);

                    // Фильтрация проблемных соседей: невалидные позиции или нулевая/NaN дистанция
                    var filtered = new List<Agent3D>(neighbors.Count);
                    bool hadOverlaps = false;
                    foreach (var n in neighbors)
                    {
                        if (!IsFiniteVec(s.Position) || !IsFiniteVec(n.Position))
                        {
                            try { LogNumericIssue("NeighborsFilter.Seeker", $"Non-finite pos: self={s.Position}, other={n.Position}"); } catch { }
                            continue;
                        }
                        float d = Vector3.Distance(s.Position, n.Position);
                        if (float.IsNaN(d) || float.IsInfinity(d) || d < 1e-5f)
                        {
                            hadOverlaps = true;
                            try { LogNumericIssue("NeighborsFilter.Seeker", $"Too close/invalid distance: d={d} self={s.Position} other={n.Position}"); } catch { }
                            continue;
                        }
                        filtered.Add(n);
                    }

                    try
                    {
                        s.MoveWithCollisionAvoidance(World, deltaTime, filtered);
                    }
                    catch (ArithmeticException ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Seeker", $"ArithmeticException: {ex.Message} self={s.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                        // пропускаем движение в этом кадре
                    }
                    catch (Exception ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Seeker", $"Exception: {ex.Message} self={s.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                }
            }
            foreach (var h in hiders)
            {
                long aNow = _currentActionHiders.TryGetValue(h, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.Forward || aNow == actCfg.ForwardLeft || aNow == actCfg.ForwardRight)
                {
                    var neighbors = new List<Agent3D>();
                    foreach (var h2 in hiders) if (!ReferenceEquals(h2, h)) neighbors.Add(h2);
                    neighbors.AddRange(seekers);

                    // Фильтрация проблемных соседей: невалидные позиции или нулевая/NaN дистанция
                    var filtered = new List<Agent3D>(neighbors.Count);
                    bool hadOverlaps = false;
                    foreach (var n in neighbors)
                    {
                        if (!IsFiniteVec(h.Position) || !IsFiniteVec(n.Position))
                        {
                            try { LogNumericIssue("NeighborsFilter.Hider", $"Non-finite pos: self={h.Position}, other={n.Position}"); } catch { }
                            continue;
                        }
                        float d = Vector3.Distance(h.Position, n.Position);
                        if (float.IsNaN(d) || float.IsInfinity(d) || d < 1e-5f)
                        {
                            hadOverlaps = true;
                            try { LogNumericIssue("NeighborsFilter.Hider", $"Too close/invalid distance: d={d} self={h.Position} other={n.Position}"); } catch { }
                            continue;
                        }
                        filtered.Add(n);
                    }

                    try
                    {
                        h.MoveWithCollisionAvoidance(World, deltaTime, filtered);
                    }
                    catch (ArithmeticException ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Hider", $"ArithmeticException: {ex.Message} self={h.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Hider", $"Exception: {ex.Message} self={h.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                }
            }

            // 5) Побочные эффекты шага: обновление визуального исследования
            foreach (var s in seekers) s.UpdateVisualExploration(World);
            foreach (var h in hiders)  h.UpdateVisualExploration(World);

            // Карта текущей видимости для всех Hider
            var hiderVisibleNow = new Dictionary<Agent3D, bool>(hiders.Count);
            foreach (var h in hiders)
                hiderVisibleNow[h] = seekers.Any(s => s.CanSee(h, World));

            // 6) Состояния ПОСЛЕ действия (state_{t+1})
            var seekerStatesAfter = new Dictionary<Agent3D, State>(seekers.Count);
            var hiderStatesAfter  = new Dictionary<Agent3D, State>(hiders.Count);

            foreach (var s in seekers)
            {
                var target = GetNearestOpponent(s, hiders);
                var ad = new SimAdapter3D(World, s, target);
                var st = ad.GetSeekerState();
                seekerStatesAfter[s] = st;
            }
            foreach (var h in hiders)
            {
                var watcher = GetNearestOpponent(h, seekers);
                var ad = new SimAdapter3D(World, watcher, h);
                var st = ad.GetHiderState();
                hiderStatesAfter[h] = st;
            }

            // 7) Награды и запись переходов за текущий шаг
            foreach (var s in seekers)
            {
                // Дельты исследования Seeker
                var prev = _prevExploreCountsSeekers.TryGetValue(s, out var p) ? p : (0, 0);
                int afterPhysical = s.GetExploredCount();
                int afterVisual   = s.GetVisuallyExploredCount();
                int newPhysical = Math.Max(0, afterPhysical - prev.Item1);
                int newVisual   = Math.Max(0, afterVisual   - prev.Item2);

                bool seesAny = hiders.Any(h => s.CanSee(h, World));
                s.IsSeeingTarget = seesAny;

                if (seesAny)
                {
                    foreach (var t in hiders.Where(h => s.CanSee(h, World)))
                        _seekersBoard.ReportSeenTarget(t, t.Position, Timer);
                }

                float reward = ComputeSeekerRewardFor(s, newPhysical, newVisual, seesAny);

                if (giveCatchBonus)
                    reward += perSeekerCatchBonus;

                long actionThisStep = _currentActionSeekers.TryGetValue(s, out var actNowS) ? actNowS : actCfg.Forward;
                if (actionThisStep == actCfg.TurnLeft || actionThisStep == actCfg.TurnRight ||
                    actionThisStep == actCfg.ForwardLeft || actionThisStep == actCfg.ForwardRight)
                    reward -= MathF.Max(0f, Config.Seeker.RotationPenalty);

                var nearestForS = GetNearestOpponent(s, hiders);
                float curDistS = Vector3.Distance(s.Position, nearestForS.Position);
                float lastDistS = _lastDistToNearestHider.TryGetValue(s, out var prevDistS) ? prevDistS : curDistS;
                if (curDistS > lastDistS - _noProgressDistanceEps && newPhysical == 0 && newVisual == 0 && !seesAny)
                    reward -= MathF.Max(0f, Config.Seeker.NoProgressPenalty);
                _lastDistToNearestHider[s] = curDistS;

                // Запись перехода: (state_t, action_t, reward_t, state_{t+1})
                var stateBefore = seekerStatesBefore[s];
                var stateAfter  = seekerStatesAfter[s];
                _seekerAgent.Store(stateBefore.ToArray(World.Size), actionThisStep, reward, stateAfter.ToArray(World.Size), isTerminalThisStep);
                _accSeekerReward += reward;

                _prevExploreCountsSeekers[s] = (afterPhysical, afterVisual);
            }

            if (giveCatchBonus) _catchBonusGiven = true;

            foreach (var h in hiders)
            {
                bool visibleNow = hiderVisibleNow[h];

                foreach (var t in seekers.Where(s => h.CanSee(s, World)))
                    _hidersBoard.ReportSeenTarget(t, t.Position, Timer);

                float reward = ComputeHiderRewardFor(h, seekers, visibleNow);

                long actionThisStep = _currentActionHiders.TryGetValue(h, out var actNowH) ? actNowH : actCfg.Forward;
                if (actionThisStep == actCfg.TurnLeft || actionThisStep == actCfg.TurnRight ||
                    actionThisStep == actCfg.ForwardLeft || actionThisStep == actCfg.ForwardRight)
                    reward -= MathF.Max(0f, Config.Hider.RotationPenalty);

                var stateBefore = hiderStatesBefore[h];
                var stateAfter  = hiderStatesAfter[h];
                _hiderAgent.Store(stateBefore.ToArray(World.Size), actionThisStep, reward, stateAfter.ToArray(World.Size), isTerminalThisStep);
                _accHiderReward += reward;

                _wasHiderVisiblePrevMap[h] = visibleNow;
            }

            // 8) Обучение (один вызов на роль)
            _seekerAgent.Learn();
            _hiderAgent.Learn();

            // Синхронизация знаний команды (union известных стен)
            MergeTeamKnowledge();

            // Метрики (оставляем в терминах «первой» пары для совместимости HUD)
            _framesInSession++;
            if (IsHiderVisible) _visibleFrames++;
            _sumDistance += Vector3.Distance(Seeker.Position, Hider.Position);

            // Раннее завершение при отсутствии прогресса (ориентируемся на «первую» пару)
            if (!IsHiderVisible)
            {
                float dist = Vector3.Distance(Seeker.Position, Hider.Position);
                float distDelta = MathF.Abs(dist - _lastDistanceForProgress);
                int visExplored = Seeker.GetVisuallyExploredCount();
                int visDelta = visExplored - _lastSeekerVisualExploredForProgress;

                if (distDelta < _noProgressDistanceEps && visDelta <= 0)
                {
                    _noProgressTimer += deltaTime;
                    if (_noProgressTimer >= _noProgressSeconds)
                    {
                        Restart();
                        return;
                    }
                }
                else
                {
                    _noProgressTimer = 0f;
                    _lastDistanceForProgress = dist;
                    _lastSeekerVisualExploredForProgress = visExplored;
                }
            }
            else
            {
                _noProgressTimer = 0f;
                _lastDistanceForProgress = Vector3.Distance(Seeker.Position, Hider.Position);
                _lastSeekerVisualExploredForProgress = Seeker.GetVisuallyExploredCount();
            }
        }

        private Agent3D GetNearestOpponent(Agent3D agent, List<Agent3D> opponents)
        {
            if (opponents == null || opponents.Count == 0) return agent.IsSeeker ? Hider : Seeker;
            Agent3D best = opponents[0];
            float bestD = Vector3.Distance(agent.Position, best.Position);
            for (int i = 1; i < opponents.Count; i++)
            {
                float d = Vector3.Distance(agent.Position, opponents[i].Position);
                if (d < bestD) { bestD = d; best = opponents[i]; }
            }
            return best;
        }

        // Цель находится в секторе и радиусе наблюдателя, но не видна (окклюзия препятствием)
        private bool IsOccludedByWall(Agent3D observer, Agent3D target)
        {
            float maxDist = observer.IsSeeker ? Config.Seeker.VisionRadius : Config.Hider.VisionRadius;
            float halfFov = (observer.IsSeeker ? Config.Seeker.VisionAngle : Config.Hider.VisionAngle) * 0.5f;

            Vector3 toTarget = target.Position - observer.Position;
            float dist = toTarget.Length();
            if (dist > maxDist) return false;
            if (dist < 1e-5f) return false;

            float yawRad = observer.Direction * MathF.PI / 180f;
            var forward = new Vector3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));
            Vector3 dir = Vector3.Normalize(toTarget);
            float dot = Math.Clamp(Vector3.Dot(forward, dir), -1f, 1f);
            float angleDeg = MathF.Acos(dot) * (180f / MathF.PI);
            if (angleDeg > halfFov) return false;

            // В секторе и в радиусе, но прямой видимости нет => окклюзия
            return !observer.CanSee(target, World);
        }

        private bool AnyHiderVisible()
        {
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };
            foreach (var h in hiders)
                foreach (var s in seekers)
                    if (s.CanSee(h, World)) return true;
            return false;
        }

        // Объединяет известные стены по командам и распространяет union обратно всем агентам и в командный blackboard
        private void MergeTeamKnowledge()
        {
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            // Seekers
            var unionS = new HashSet<(int x, int z)>(_seekersBoard.KnownWalls);
            foreach (var s in seekers) unionS.UnionWith(s.KnownWalls);
            _seekersBoard.KnownWalls.UnionWith(unionS);
            foreach (var s in seekers) s.KnownWalls.UnionWith(_seekersBoard.KnownWalls);

            // Hiders
            var unionH = new HashSet<(int x, int z)>(_hidersBoard.KnownWalls);
            foreach (var h in hiders) unionH.UnionWith(h.KnownWalls);
            _hidersBoard.KnownWalls.UnionWith(unionH);
            foreach (var h in hiders) h.KnownWalls.UnionWith(_hidersBoard.KnownWalls);
        }

        private float ComputeSeekerRewardFor(Agent3D s, int newPhysical, int newVisual, bool seesAny)
        {
            float r = seesAny ? Config.Seeker.RewardWhenHiderVisible : Config.Seeker.RewardWhenHiderHidden;

            float expPhysBonus   = newPhysical * Config.Seeker.PhysicalExploreReward;
            float expVisualBonus = newVisual   * Config.Seeker.VisualExploreReward;
            r += expPhysBonus + expVisualBonus;
            ExplorationScore += expPhysBonus + expVisualBonus;

            // Вклад за изменение дистанции до ближайшего Hider (положительный при сближении)
            var hidersList = (Hiders != null && Hiders.Count > 0) ? Hiders : new List<Agent3D> { Hider };
            var nearestH = GetNearestOpponent(s, hidersList);
            float curDist = Vector3.Distance(s.Position, nearestH.Position);
            float lastDist = _lastDistToNearestHider.TryGetValue(s, out var prevDist) ? prevDist : curDist;
            float distDeltaToward = lastDist - curDist; // >0 если приблизился
            r += distDeltaToward * MathF.Max(0f, Config.Seeker.ProximityRewardMultiplier);

            if (float.IsNaN(r) || float.IsInfinity(r))
                throw new Exception($"[NaN/Inf] ComputeSeekerRewardFor: {r}");
            return r;
        }

        private float ComputeHiderRewardFor(Agent3D h, List<Agent3D> seekers, bool visibleNow)
        {
            float reward = 0f;
            if (visibleNow) reward -= Config.Hider.RewardWhenVisible;
            else            reward += Config.Hider.RewardWhenHidden;

            // расстояние до ближайшего seeker
            var nearest = GetNearestOpponent(h, seekers);
            float currentDistance = Vector3.Distance(nearest.Position, h.Position);
            float lastDist = _lastDistToNearestSeeker.TryGetValue(h, out var prev) ? prev : currentDistance;

            // Вклад за изменение дистанции (положительный при удалении)
            float distDeltaAway = currentDistance - lastDist; // >0 если удалился
            reward += distDeltaAway * MathF.Max(0f, Config.Hider.ProximityRewardMultiplier);

            if (currentDistance > lastDist) reward += Config.Hider.RewardWhenIncreasingDistance;
            else if (currentDistance <= lastDist + _noProgressDistanceEps) reward -= MathF.Max(0f, Config.Hider.NoProgressPenalty);
            _lastDistToNearestSeeker[h] = currentDistance;

            // Бонус за «скрыт за стеной» только если цель в секторе и радиусе, но невидима из-за препятствия
            if (!visibleNow && IsOccludedByWall(h, nearest))
                reward += Config.Hider.RewardWhenHiddenBehindWall;

            if (_wasHiderVisiblePrevMap.TryGetValue(h, out var wasVisible) && wasVisible && !visibleNow)
                reward += Config.Hider.EscapeBonus;

            if (float.IsNaN(reward) || float.IsInfinity(reward))
                throw new Exception($"[NaN/Inf] ComputeHiderRewardFor: {reward}");
            return reward;
        }

        private void AppendSessionMetrics()
        {
            try
            {
                string logsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "logs");
                Directory.CreateDirectory(logsDir);
                string file = Path.Combine(logsDir, "metrics.csv");
                bool writeHeader = !File.Exists(file);
                using (var sw = new StreamWriter(file, append: true))
                {
                    if (writeHeader)
                        sw.WriteLine("total_session,session_time,caught,visibility_ratio,avg_distance,seeker_physical,seeker_visual,seeker_total,acc_seeker_reward,acc_hider_reward");
                    float visibilityRatio = _framesInSession > 0 ? (float)_visibleFrames / _framesInSession : 0f;
                    float avgDistance = _framesInSession > 0 ? _sumDistance / _framesInSession : 0f;
                    sw.WriteLine($"{TotalSessions},{Timer:F3},{_isHiderCaught},{visibilityRatio:F3},{avgDistance:F3},{Seeker.GetExploredCount()},{Seeker.GetVisuallyExploredCount()},{Seeker.GetTotalExploredCount()},{_accSeekerReward:F3},{_accHiderReward:F3}");
                }
            }
            catch { }
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
            if (float.IsNaN(SeekerScore) || float.IsInfinity(SeekerScore))
                throw new Exception($"[NaN/Inf] SeekerScore: {SeekerScore}");
            if (float.IsNaN(HiderScore) || float.IsInfinity(HiderScore))
                throw new Exception($"[NaN/Inf] HiderScore: {HiderScore}");
        }

        public void Restart()
        {
            _justRestarted = true;

            // Log previous session metrics before resetting
            if (_framesInSession > 0)
                AppendSessionMetrics();

            Session++;
            TotalSessions++;
            SaveTotalSessions();

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = false;

            // reset metrics
            _framesInSession = 0;
            _visibleFrames = 0;
            _sumDistance = 0f;
            _accSeekerReward = 0f;
            _accHiderReward = 0f;
            _noProgressTimer = 0f;
            _lastDistanceForProgress = 0f;
            _lastSeekerVisualExploredForProgress = 0;

            World.GenerateStaticGrid();

            // Новый эпизод — очищаем общие знания команд
            _seekersBoard.Clear();
            _hidersBoard.Clear();

            Vector3 seekerPos = World.GetRandomValidAgentPosition(Config.Seeker.AgentRadius, 0f);
            Vector3 hiderPos = World.GetRandomValidAgentPosition(Config.Hider.AgentRadius, 0f);
            int attempts = 0;
            while (attempts < 50 && Vector3.Distance(seekerPos, hiderPos) < Config.MinInitialSeparation)
            {
                hiderPos = World.GetRandomValidAgentPosition(Config.Hider.AgentRadius, 0f);
                attempts++;
            }
            CheckNaN(seekerPos, "Restart:seekerPos");
            CheckNaN(hiderPos, "Restart:hiderPos");

            Seeker.Position = seekerPos;
            Seeker.Direction = Raylib.GetRandomValue(0, 359);
            Seeker.InitWorldSize(World.Size);
            Seeker.SetWorld(World);
            Seeker.TeamBoard = _seekersBoard;

            Hider.Position = hiderPos;
            Hider.Direction = Raylib.GetRandomValue(0, 359);
            Hider.InitWorldSize(World.Size);
            Hider.SetWorld(World);
            Hider.TeamBoard = _hidersBoard;

            // Если коллекции заданы — респавним всех
            if (Seekers != null && Seekers.Count > 0)
            {
                for (int i = 0; i < Seekers.Count; i++)
                {
                    Vector3 pos = World.GetRandomValidAgentPosition(Config.Seeker.AgentRadius, 0f);
                    Seekers[i].Position = pos;
                    Seekers[i].Direction = Raylib.GetRandomValue(0, 359);
                    Seekers[i].InitWorldSize(World.Size);
                    Seekers[i].SetWorld(World);
                    Seekers[i].TeamBoard = _seekersBoard;
                }
                // Гарантируем, что «первый» совпадает с основным
                Seekers[0].Position = Seeker.Position;
                Seekers[0].Direction = Seeker.Direction;
            }

            if (Hiders != null && Hiders.Count > 0)
            {
                for (int i = 0; i < Hiders.Count; i++)
                {
                    Vector3 pos = World.GetRandomValidAgentPosition(Config.Hider.AgentRadius, 0f);
                    Hiders[i].Position = pos;
                    Hiders[i].Direction = Raylib.GetRandomValue(0, 359);
                    Hiders[i].InitWorldSize(World.Size);
                    Hiders[i].SetWorld(World);
                    Hiders[i].TeamBoard = _hidersBoard;
                }
                Hiders[0].Position = Hider.Position;
                Hiders[0].Direction = Hider.Direction;
            }

            // Полный сброс исследования и знаний для нового эпизода
            Seeker.ResetExploration();
            Seeker.KnownWalls.Clear();
            Hider.ResetExploration();
            Hider.KnownWalls.Clear();

            if (Seekers != null && Seekers.Count > 0)
            {
                foreach (var s in Seekers)
                {
                    s.ResetExploration();
                    s.KnownWalls.Clear();
                }
            }
            if (Hiders != null && Hiders.Count > 0)
            {
                foreach (var h in Hiders)
                {
                    h.ResetExploration();
                    h.KnownWalls.Clear();
                }
            }

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();

            // Очистка per-agent структур при новом эпизоде
            _prevStateSeekers.Clear();
            _prevStateHiders.Clear();
            _prevActionSeekers.Clear();
            _prevActionHiders.Clear();
            _repeatLeftSeekers.Clear();
            _repeatLeftHiders.Clear();
            _currentActionSeekers.Clear();
            _currentActionHiders.Clear();
            _lastDistToNearestSeeker.Clear();
            _wasHiderVisiblePrevMap.Clear();
            _prevExploreCountsSeekers.Clear();
            _lastDistToNearestHider.Clear();
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
            Seeker.SetWorld(World);
            Seeker.TeamBoard = _seekersBoard;
            Hider.InitWorldSize(World.Size);
            Hider.SetWorld(World);
            Hider.TeamBoard = _hidersBoard;

            // Убедимся, что новые агенты стоят на валидных клетках мира симуляции
            EnsureAgentOnValidCell(Seeker);
            EnsureAgentOnValidCell(Hider);

            // Полный сброс исследования и знаний
            Seeker.ResetExploration();
            Seeker.KnownWalls.Clear();
            Hider.ResetExploration();
            Hider.KnownWalls.Clear();

            if (Seekers != null && Seekers.Count > 0)
            {
                foreach (var s in Seekers)
                {
                    s.ResetExploration();
                    s.KnownWalls.Clear();
                }
            }
            if (Hiders != null && Hiders.Count > 0)
            {
                foreach (var h in Hiders)
                {
                    h.ResetExploration();
                    h.KnownWalls.Clear();
                }
            }

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();

            // Сброс per-agent структур
            _prevStateSeekers.Clear();
            _prevStateHiders.Clear();
            _prevActionSeekers.Clear();
            _prevActionHiders.Clear();
            _repeatLeftSeekers.Clear();
            _repeatLeftHiders.Clear();
            _currentActionSeekers.Clear();
            _currentActionHiders.Clear();
            _lastDistToNearestSeeker.Clear();
            _wasHiderVisiblePrevMap.Clear();
            _prevExploreCountsSeekers.Clear();
            _lastDistToNearestHider.Clear();

            CheckNaN(Seeker.Position, "Reset:Seeker.Position");
            CheckNaN(Hider.Position, "Reset:Hider.Position");
        }

        public void Draw()
        {
            // Защита от NaN/Inf в камере и позициях агентов
            SanitizeScene();

            Raylib.BeginMode3D(_camera);
            {
                World.Draw(true);
                if (_showGrid) World.DrawGrid();

                // Рисуем всех агентов, если заданы списки; иначе — одиночные
                if (Seekers != null && Seekers.Count > 0)
                {
                    foreach (var s in Seekers) s.Draw();
                }
                else
                {
                    Seeker.Draw();
                }

                if (Hiders != null && Hiders.Count > 0)
                {
                    foreach (var h in Hiders) h.Draw();
                }
                else
                {
                    Hider.Draw();
                }

                if (_showVisionCones)
                {
                    // Конусы взглядов рисуются непосредственно в Agent3D.Draw
                }
            }
            Raylib.EndMode3D();

            DrawHUD();
        }

        private void DrawHUD()
        {
            // Параметры оформления (уменьшенные шрифты)
            int pad = 8;
            int headerFont = 18;
            int lineFont = 16;
            int barHeight = 8;
            int barWidth = 90; // короче, чтобы не закрывать текст очков
            int headerStep = headerFont + 6;
            int lineStep = lineFont + 4;

            // Данные по командам
            var seekersList = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hidersList  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            int seekersCount = seekersList.Count;
            int hidersCount  = hidersList.Count;

            int seekersSeeing = 0;
            foreach (var s in seekersList)
                if (hidersList.Any(h => s.CanSee(h, World))) seekersSeeing++;

            int visibleHiders = 0;
            foreach (var h in hidersList)
                if (seekersList.Any(s => s.CanSee(h, World))) visibleHiders++;

            // Формируем строки
            string l1 = $"Session: {Session} / Total: {TotalSessions}";
            Color timeColor = Timer > (sessionDurationSeconds * 0.9f) ? Color.Red : Color.White;
            string l2 = $"Time: {Timer:F1}s / {sessionDurationSeconds:F0}s";

            string sLine = $"Seekers: {seekersCount}  |  Seeing: {seekersSeeing}";
            string sScore = $"Score: {SeekerScore:F1}";
            float seekerPercent = MathF.Max(0f, MathF.Min(1f, sessionDurationSeconds > 0f ? SeekerScore / sessionDurationSeconds : 0f));

            string hLine = $"Hiders: {hidersCount}  |  Visible: {visibleHiders}";
            string hScore = $"Score: {HiderScore:F1}";
            float hiderPercent = MathF.Max(0f, MathF.Min(1f, sessionDurationSeconds > 0f ? HiderScore / sessionDurationSeconds : 0f));

            float distance = Vector3.Distance(Seeker.Position, Hider.Position);
            string distLine = $"Distance (S0-H0): {distance:F1}";

            string visibilityText = IsHiderVisible ? "VISIBLE" : "HIDDEN";

            // Доп. метрики для баров
            float timePercent = Math.Clamp(sessionDurationSeconds > 0f ? (Timer / sessionDurationSeconds) : 0f, 0f, 1f);
            float tsHud = (!float.IsFinite(Config.TimeScale) || Config.TimeScale <= 0f) ? 1.0f : Config.TimeScale;
            int effectiveFramesForCatch = Math.Max(1, (int)MathF.Round(Config.FramesForCatch / tsHud));
            string catchLine = $"Catch: {_caughtFrames}/{effectiveFramesForCatch}";
            int l2W = Raylib.MeasureText(l2, headerFont);
            int catchW = Raylib.MeasureText(catchLine, lineFont);

            // Подсчет размеров подложки
            int maxTextW = 0;
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(l1, headerFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(l2, headerFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(sLine, lineFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(sScore, lineFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(hLine, lineFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(hScore, lineFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(distLine, lineFont));
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText($"Hiders: {visibilityText}", lineFont));

            int sScoreW = Raylib.MeasureText(sScore, lineFont);
            int hScoreW = Raylib.MeasureText(hScore, lineFont);
            int barPad = 10;

            // Учтем ширину прогресс-баров: начинаются после текста
            int contentW = Math.Max(
                maxTextW,
                Math.Max(
                    Math.Max(sScoreW + barPad + barWidth, hScoreW + barPad + barWidth),
                    Math.Max(l2W + barPad + barWidth, catchW + barPad + barWidth)
                )
            );
            int boxW = pad * 2 + contentW;

            // Высота: 2 заголовка + бар времени + 2 секции с барами + дистанция + видимость + бар поимки + (опц.) CAUGHT
            int boxH = pad * 2
                       + headerStep * 2
                       + (barHeight + 6) // Time bar
                       + lineStep // Seekers line
                       + (barHeight + 6) // Seekers bar
                       + lineStep // Hiders line
                       + (barHeight + 6) // Hiders bar
                       + lineStep // Distance
                       + lineStep // Visibility
                       + (barHeight + 6); // Catch bar
            if (_isHiderCaught) boxH += (lineFont + 8);

            // Подложка
            Raylib.DrawRectangle(5, 5, boxW, boxH, new Color(0, 0, 0, 180));

            int x = 5 + pad;
            int y = 5 + pad;

            // Заголовки
            Raylib.DrawText(l1, x, y, headerFont, Color.White); y += headerStep;

            // Время + бар времени (рисуем бар до смещения y)
            Raylib.DrawText(l2, x, y, headerFont, timeColor);
            int barXTime = x + l2W + barPad;
            int barYTime = y + (headerFont / 2) - (barHeight / 2);
            var timeBarColor = new Color(100, 200, 255, 255);
            Raylib.DrawRectangle(barXTime, barYTime, (int)(barWidth * timePercent), barHeight, timeBarColor);
            Raylib.DrawRectangleLines(barXTime, barYTime, barWidth, barHeight, Color.White);
            y += headerStep;
            y += (barHeight + 6);

            // Секция Seekers
            var seekerColor = new Color(60, 120, 255, 255);
            Raylib.DrawText(sLine, x, y, lineFont, seekerColor); y += lineStep;
            Raylib.DrawText(sScore, x, y, lineFont, seekerColor);
            // Прогресс-бар по команде Seeker: ставим после текста очков
            int barX = x + sScoreW + barPad;
            int barY = y + (lineFont / 2) - (barHeight / 2);
            Raylib.DrawRectangle(barX, barY, (int)(barWidth * seekerPercent), barHeight, seekerColor);
            Raylib.DrawRectangleLines(barX, barY, barWidth, barHeight, Color.White);
            y += (barHeight + 6);

            // Секция Hiders
            var hiderColor = new Color(40, 200, 60, 255);
            Raylib.DrawText(hLine, x, y, lineFont, hiderColor); y += lineStep;
            Raylib.DrawText(hScore, x, y, lineFont, hiderColor);
            barX = x + hScoreW + barPad;
            barY = y + (lineFont / 2) - (barHeight / 2);
            Raylib.DrawRectangle(barX, barY, (int)(barWidth * hiderPercent), barHeight, hiderColor);
            Raylib.DrawRectangleLines(barX, barY, barWidth, barHeight, Color.White);
            y += (barHeight + 6);

            // Доп. строки
            Raylib.DrawText(distLine, x, y, lineFont, new Color(160, 160, 160, 255)); y += lineStep;
            Color visibilityColor = IsHiderVisible ? Color.Red : new Color(0, 200, 60, 255);
            Raylib.DrawText($"Hiders: {visibilityText}", x, y, lineFont, visibilityColor); y += lineStep;

            // Прогресс поимки (накапливаемые кадры видимости)
            var catchColor = new Color(255, 140, 0, 255);
            Raylib.DrawText(catchLine, x, y, lineFont, catchColor);
            int barXC = x + catchW + barPad;
            int barYC = y + (lineFont / 2) - (barHeight / 2);
            float catchPercent = Math.Clamp(effectiveFramesForCatch > 0 ? (_caughtFrames / (float)effectiveFramesForCatch) : 0f, 0f, 1f);
            Raylib.DrawRectangle(barXC, barYC, (int)(barWidth * catchPercent), barHeight, catchColor);
            Raylib.DrawRectangleLines(barXC, barYC, barWidth, barHeight, Color.White);
            y += (barHeight + 6);

            if (_isHiderCaught)
                Raylib.DrawText("CAUGHT!", x, y, 18, Color.Red);
        }

        // Позволяет задать списки агентов после создания симуляции
        public void SetAgents(List<Agent3D> seekers, List<Agent3D> hiders)
        {
            Seekers = seekers ?? new List<Agent3D>();
            Hiders = hiders ?? new List<Agent3D>();

            if (Seekers.Count > 0) Seeker = Seekers[0];
            if (Hiders.Count > 0) Hider = Hiders[0];

            foreach (var s in Seekers) { s.InitWorldSize(World.Size); s.SetWorld(World); }
            foreach (var h in Hiders) { h.InitWorldSize(World.Size); h.SetWorld(World); }

            foreach (var s in Seekers) s.TeamBoard = _seekersBoard;
            foreach (var h in Hiders) h.TeamBoard = _hidersBoard;

            // Валидируем позиции всех агентов относительно мира симуляции
            foreach (var s in Seekers) EnsureAgentOnValidCell(s);
            foreach (var h in Hiders) EnsureAgentOnValidCell(h);
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

        // Централизованное логирование числовых проблем (NaN/Inf)
        private void LogNumericIssue(string tag, string details)
        {
            try
            {
                string logsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "logs");
                Directory.CreateDirectory(logsDir);
                string file = Path.Combine(logsDir, "numeric_issues.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {details}";
                File.AppendAllLines(file, new[] { line });
            }
            catch { }
        }

        private static string FormatVec(Vector3 v)
        {
            bool bad = !(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
            return $"(X={v.X}, Y={v.Y}, Z={v.Z}){(bad ? " [BAD]" : "")}";
        }

        // Подробная диагностика состояния симуляции
        public void DumpDiagnostics(Exception? ex = null)
        {
            try
            {
                string logsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "logs");
                Directory.CreateDirectory(logsDir);
                string file = Path.Combine(logsDir, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");

                var sb = new StringBuilder(4096);
                sb.AppendLine("==== Simulation3D Diagnostics ====");
                sb.AppendLine($"Time: {DateTime.Now:O}");
                if (ex != null)
                {
                    sb.AppendLine("Exception:");
                    sb.AppendLine(ex.ToString());
                }

                sb.AppendLine();
                sb.AppendLine($"Session: {Session}  TotalSessions: {TotalSessions}");
                sb.AppendLine($"Timer: {Timer:F6}  SessionDurationSeconds: {sessionDurationSeconds:F6}");
                sb.AppendLine($"Scores: Seeker={SeekerScore:F6} Hider={HiderScore:F6} Exploration={ExplorationScore:F6}");
                sb.AppendLine($"Flags: IsHiderVisible={IsHiderVisible} IsHiderCaught={_isHiderCaught} CaughtFrames={_caughtFrames}");
                sb.AppendLine($"VisibilityCheck: last={_lastVisibilityCheck:F6} interval={_visibilityCheckInterval:F6}");
                sb.AppendLine($"NoProgress: timer={_noProgressTimer:F6} lastDist={_lastDistanceForProgress:F6} eps={_noProgressDistanceEps:F6} seconds={_noProgressSeconds:F6}");
                sb.AppendLine($"ActionRepeat={_actionRepeat}");

                // Конфиг-критичные параметры
                try
                {
                    float ts = (!float.IsFinite(Config?.TimeScale ?? 1f) || (Config?.TimeScale ?? 1f) <= 0f) ? 1f : (Config?.TimeScale ?? 1f);
                    int framesThreshold = Math.Max(1, (int)MathF.Round((Config?.FramesForCatch ?? 1) / ts));
                    sb.AppendLine("Config:");
                    sb.AppendLine($"  TimeScale={Config?.TimeScale}");
                    sb.AppendLine($"  FramesForCatch={Config?.FramesForCatch} -> effective={framesThreshold}");
                    sb.AppendLine($"  Seeker.RotationStepDegrees={Config?.Seeker.RotationStepDegrees}");
                    sb.AppendLine($"  Hider.RotationStepDegrees={Config?.Hider.RotationStepDegrees}");
                }
                catch (Exception cex)
                {
                    sb.AppendLine($"[WARN] Failed to read Config details: {cex.Message}");
                }

                // Мир и камера
                try
                {
                    sb.AppendLine();
                    sb.AppendLine("World/Camera:");
                    sb.AppendLine($"  World.Size={World?.Size}");
                    sb.AppendLine($"  Camera.Position={FormatVec(_camera.Position)}");
                    sb.AppendLine($"  Camera.Target={FormatVec(_camera.Target)}");
                    sb.AppendLine($"  Camera.Up={FormatVec(_camera.Up)}  FovY={_camera.FovY}");
                }
                catch (Exception wex)
                {
                    sb.AppendLine($"[WARN] Failed to dump world/camera: {wex.Message}");
                }

                // Агенты
                try
                {
                    var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
                    var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

                    sb.AppendLine();
                    sb.AppendLine($"Seekers ({seekers.Count}):");
                    for (int i = 0; i < seekers.Count; i++)
                    {
                        var s = seekers[i];
                        sb.AppendLine($"  S[{i}] Pos={FormatVec(s.Position)} Dir={s.Direction} IsSeeingTarget={s.IsSeeingTarget}");
                    }

                    sb.AppendLine($"Hiders ({hiders.Count}):");
                    for (int i = 0; i < hiders.Count; i++)
                    {
                        var h = hiders[i];
                        sb.AppendLine($"  H[{i}] Pos={FormatVec(h.Position)} Dir={h.Direction} IsSeeingTarget={h.IsSeeingTarget}");
                    }
                }
                catch (Exception aex)
                {
                    sb.AppendLine($"[WARN] Failed to dump agents: {aex.Message}");
                }

                // Метрики
                sb.AppendLine();
                sb.AppendLine("Metrics:");
                sb.AppendLine($"  FramesInSession={_framesInSession}  VisibleFrames={_visibleFrames}  SumDistance={_sumDistance:F6}");
                sb.AppendLine($"  AccSeekerReward={_accSeekerReward:F6}  AccHiderReward={_accHiderReward:F6}");

                // Внутренние карты (только размеры)
                try
                {
                    sb.AppendLine();
                    sb.AppendLine("Internal maps:");
                    sb.AppendLine($"  _prevStateSeekers={_prevStateSeekers.Count}");
                    sb.AppendLine($"  _prevStateHiders={_prevStateHiders.Count}");
                    sb.AppendLine($"  _prevActionSeekers={_prevActionSeekers.Count}");
                    sb.AppendLine($"  _prevActionHiders={_prevActionHiders.Count}");
                    sb.AppendLine($"  _repeatLeftSeekers={_repeatLeftSeekers.Count}");
                    sb.AppendLine($"  _repeatLeftHiders={_repeatLeftHiders.Count}");
                    sb.AppendLine($"  _currentActionSeekers={_currentActionSeekers.Count}");
                    sb.AppendLine($"  _currentActionHiders={_currentActionHiders.Count}");
                    sb.AppendLine($"  _lastDistToNearestSeeker={_lastDistToNearestSeeker.Count}");
                    sb.AppendLine($"  _lastDistToNearestHider={_lastDistToNearestHider.Count}");
                    sb.AppendLine($"  _prevExploreCountsSeekers={_prevExploreCountsSeekers.Count}");
                }
                catch (Exception mex)
                {
                    sb.AppendLine($"[WARN] Failed to dump maps: {mex.Message}");
                }

                // Сохранение
                File.WriteAllText(file, sb.ToString());

                try
                {
                    Console.WriteLine($"[DEBUG] Диагностика сохранена: {file}");
                }
                catch { }
            }
            catch (Exception ex2)
            {
                try
                {
                    Console.WriteLine($"[ERROR] Не удалось сохранить диагностику: {ex2}");
                }
                catch { }
            }
        }

        public static void ForceSaveTotalSessions() => SaveTotalSessions();
    }
}
