using System;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Raylib_cs;
using System.Linq;
using System.Collections.Generic;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.IO;
using HideAndSeek.Core.RL;

namespace HideAndSeek.Core.RaylibThreeD
{
    public partial class Simulation3D
    {
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
        private float _visibilityCheckInterval;

        private static string SessionCounterFile => Path.Combine(PathService.GetQtablesDirectory(), "total_sessions.json");

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


        public Simulation3D(
            Agent3D seeker,
            Agent3D hider,
            DQNAgent seekerAgent,
            DQNAgent hiderAgent)
        {
            // Теперь используем GameConfig.Instance (все параметры уже загружены)
            Config = GameConfig.Instance;
            sessionDurationSeconds = Config.SessionDurationSeconds;

            // Параметры из конфига (вместо хардкода)
            _visibilityCheckInterval = MathF.Max(0.001f, Config.VisibilityCheckInterval);
            _actionRepeat = Math.Max(1, Config.ActionRepeat);
            _noProgressDistanceEps = Math.Max(0f, Config.NoProgressDistanceEps);
            _noProgressSeconds = Math.Max(0f, Config.NoProgressSeconds);

            // Создаём мир строго по размеру из конфига, чтобы избежать расхождений
            int worldSize = Config.World.GridSize;
            World = new World3D(worldSize);
            // World3D конструктор уже вызывает GenerateStaticGrid() с параметрами из конфига;
            // повторный вызов безопасен, но не обязателен. Оставим без повторного вызова.

            Seeker = seeker;
            Seeker.InitWorldSize(World.Size);
            Seeker.SetWorld(World);

            Hider = hider;
            Hider.InitWorldSize(World.Size);
            Hider.SetWorld(World);

            // Убедимся, что стартовые позиции валидны в текущем мире симуляции
            EnsureAgentOnValidCell(Seeker);
            EnsureAgentOnValidCell(Hider);
            // Запомним стартовые валидные позиции
            _lastValidPos.Clear();
            RememberAllAgentsValidPositions();

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





        // Action repeat and progress tracking
        private int _actionRepeat;

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
        private float _noProgressDistanceEps = 0f;
        private float _noProgressSeconds = 0f;

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
        // Накапливаемый штраф за отсутствие прогресса для каждого Seeker (растёт по шагам до лимита)
        private readonly Dictionary<Agent3D, float> _noProgressPenaltyAccumSeekers = new();

        // Last known valid positions to avoid random teleports during draw-time sanitization
        private readonly Dictionary<Agent3D, Vector3> _lastValidPos = new();

        public bool EnableLearning { get; set; } = true;

        // (Удалено) Ранее здесь находилась временная legacy-реализация UpdateRLAgents, заменённая на основную в Simulation3D.RL.cs







    }
}
