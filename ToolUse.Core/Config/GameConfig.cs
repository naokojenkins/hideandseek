using System;
using System.IO;
using Newtonsoft.Json;

namespace ToolUse.Core.Config
{
    /// <summary>
    /// Главный конфиг всей симуляции: параметры мира, агентов, DQN и наград.
    /// Используйте GameConfig.Instance для доступа к единому объекту конфигурации во всём проекте!
    /// </summary>
    public class GameConfig
    {
        private static GameConfig? _instance;
        /// <summary> Schema version for game_config.json to allow migrations. </summary>
        public int Version { get; set; } = 2;

        /// <summary>
        /// Единый глобальный экземпляр конфига (лениво загружается из файла один раз).
        /// </summary>
        public static GameConfig Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        /// <summary>
        /// Allows setting the singleton instance from external configuration/bootstrap logic.
        /// </summary>
        public static void SetInstance(GameConfig cfg)
        {
            _instance = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        /// <summary>
        /// Путь к файлу конфигурации (можно переопределить при необходимости).
        /// </summary>
        public static string ConfigPath { get; set; } = "game_config.json";

        /// <summary>
        /// Параметры мира (размеры, клетки, стены).
        /// </summary>
        public WorldConfig World { get; set; } = new WorldConfig();

        /// <summary>
        /// Параметры агента Seeker.
        /// </summary>
        public AgentConfig Seeker { get; set; } = new AgentConfig();

        /// <summary>
        /// Параметры агента Hider.
        /// </summary>
        public AgentConfig Hider { get; set; } = new AgentConfig();

        /// <summary>
        /// Параметры DQN (структура, lr, gamma, replay buffer).
        /// Legacy: kept for backward compatibility. Prefer Training/Model/ReplayBuffer sections.
        /// </summary>
        public DQNConfig DQN { get; set; } = new DQNConfig();

        /// <summary>
        /// New, more structured configuration sections. If not present in JSON, they will be auto-filled from DQN.
        /// </summary>
        public TrainingConfig Training { get; set; } = new TrainingConfig();
        public ModelConfig Model { get; set; } = new ModelConfig();
        public ReplayBufferConfig ReplayBuffer { get; set; } = new ReplayBufferConfig();

        /// <summary>
        /// Пространство действий (семантика и количество действий).
        /// </summary>
        public ActionSpaceConfig Actions { get; set; } = new ActionSpaceConfig();

        /// <summary>
        /// Длительность сессии (сек).
        /// </summary>
        public float SessionDurationSeconds { get; set; } = 30f;

        /// <summary>
        /// Сколько кадров подряд надо держать Hider в поле зрения для поимки.
        /// </summary>
        public int FramesForCatch { get; set; } = 60;

        /// <summary>
        /// Seed для всех случайных событий (Random) — используйте для повторяемости экспериментов!
        /// </summary>
        public int Seed { get; set; } = 12345;

        // --- Параметры симуляции (ранее были захардкожены) ---
        /// <summary> Повтор действий (action repeat) для среды. </summary>
        public int ActionRepeat { get; set; } = 2;
        /// <summary> Интервал проверки видимости (сек). </summary>
        public float VisibilityCheckInterval { get; set; } = 0.05f;
        /// <summary> Порог изменения дистанции для детектора «нет прогресса». </summary>
        public float NoProgressDistanceEps { get; set; } = 0.05f;
        /// <summary> Время без прогресса до рестарта эпизода (сек). </summary>
        public float NoProgressSeconds { get; set; } = 5f;
        /// <summary> Минимальная стартовая дистанция между Seeker и Hider при ресете. </summary>
        public float MinInitialSeparation { get; set; } = 5f;
        /// <summary> Коэффициент сжатия времени (1 — без сжатия). </summary>
        public float TimeScale { get; set; } = 1.0f;

        /// <summary>
        /// Приватный загрузчик (используйте только через Instance).
        /// </summary>
        private static GameConfig Load(string? path = null)
        {
            // Resolve path without touching GameConfig-dependent services to avoid recursion
            string resolvedPath = SafeResolveConfigPath(path ?? ConfigPath);

            static string SafeResolveConfigPath(string fileName)
            {
                try
                {
                    if (System.IO.Path.IsPathFullyQualified(fileName))
                    {
                        var abs = System.IO.Path.GetFullPath(fileName);
                        if (File.Exists(abs)) return abs;
                    }

                    // Try under base directory /configs first, then base directory
                    string baseDir = AppContext.BaseDirectory;
                    string underConfigs = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "configs", fileName));
                    if (File.Exists(underConfigs)) return underConfigs;

                    string underBase = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, fileName));
                    if (File.Exists(underBase)) return underBase;

                    // Fallback: current working directory
                    return System.IO.Path.GetFullPath(fileName);
                }
                catch
                {
                    return fileName;
                }
            }

            try
            {
                if (File.Exists(resolvedPath))
                {
                    using var fs = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sr = new StreamReader(fs);
                    string json = sr.ReadToEnd();
                    var cfg = JsonConvert.DeserializeObject<GameConfig>(json) ?? new GameConfig();
                    MigrateIfNeeded(cfg);
                    NormalizeConfig(cfg);
                    return cfg;
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Config file not found: {resolvedPath}, using defaults");
                    var cfg = new GameConfig();
                    NormalizeConfig(cfg);
                    return cfg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load config from '{resolvedPath}': {ex.Message}");
                var cfg = new GameConfig();
                NormalizeConfig(cfg);
                return cfg;
            }

            // Локальные помощники нормализации: приводят знаки к целям ролей
            void NormalizeConfig(GameConfig cfg)
            {
                bool seekerChanged = NormalizeSeeker(cfg.Seeker);
                bool hiderChanged  = NormalizeHider(cfg.Hider);
                if (seekerChanged) Console.WriteLine("[CONFIG] Normalized Seeker rewards/signs to match hide-and-seek objectives.");
                if (hiderChanged)  Console.WriteLine("[CONFIG] Normalized Hider rewards/signs to match hide-and-seek objectives.");
            }

            void MigrateIfNeeded(GameConfig cfg)
            {
                // When Version is missing or < 2, map legacy DQN fields into new sections
                if (cfg.Version < 2)
                {
                    Console.WriteLine("[CONFIG] Migrating legacy configuration to v2 schema...");
                    cfg.Training ??= new TrainingConfig();
                    cfg.Model ??= new ModelConfig();
                    cfg.ReplayBuffer ??= new ReplayBufferConfig();

                    // Map from legacy DQN
                    cfg.Model.Hidden1 = cfg.DQN.Hidden1;
                    cfg.Model.Hidden2 = cfg.DQN.Hidden2;
                    cfg.Model.Gamma = cfg.DQN.Gamma;
                    cfg.Training.BatchSize = cfg.DQN.BatchSize;
                    cfg.ReplayBuffer.Size = cfg.DQN.ReplayBufferSize;
                    cfg.ReplayBuffer.WarmupSize = cfg.DQN.WarmupSize;
                    cfg.Training.StepsPerUpdate = cfg.DQN.StepsPerUpdate;
                    cfg.Model.LearningRate = cfg.DQN.LearningRate;
                    cfg.Model.UseHuberLoss = cfg.DQN.UseHuberLoss;
                    cfg.Model.MaxGradNorm = cfg.DQN.MaxGradNorm;
                    cfg.Model.UseAdamW = cfg.DQN.UseAdamW;
                    cfg.Model.WeightDecay = cfg.DQN.WeightDecay;
                    cfg.Model.UpdateTargetEvery = cfg.DQN.UpdateTargetEvery;
                    cfg.Model.UseSoftTarget = cfg.DQN.UseSoftTarget;
                    cfg.Model.TargetUpdateTau = cfg.DQN.TargetUpdateTau;
                    cfg.Model.RewardClipAbs = cfg.DQN.RewardClipAbs;
                    cfg.Model.RewardScale = cfg.DQN.RewardScale;
                    cfg.ReplayBuffer.BetaStart = cfg.DQN.BetaStart;
                    cfg.ReplayBuffer.BetaEnd = cfg.DQN.BetaEnd;
                    cfg.ReplayBuffer.BetaFrames = cfg.DQN.BetaFrames;
                    cfg.ReplayBuffer.UseStratifiedSampling = cfg.DQN.UseStratifiedSampling;

                    cfg.Version = 2;
                    Console.WriteLine("[CONFIG] Migration complete (schema v2).");
                }
            }

            bool NormalizeSeeker(AgentConfig a)
            {
                bool changed = false;

                // Видимость Hider должна поощряться
                if (a.RewardWhenHiderVisible < 0) { a.RewardWhenHiderVisible = Math.Abs(a.RewardWhenHiderVisible); changed = true; }
                if (a.PointsPerSecondWhenHiderVisible < 0) { a.PointsPerSecondWhenHiderVisible = Math.Abs(a.PointsPerSecondWhenHiderVisible); changed = true; }

                // Скрытность Hider должна штрафоваться
                if (a.RewardWhenHiderHidden > 0) { a.RewardWhenHiderHidden = -Math.Abs(a.RewardWhenHiderHidden); changed = true; }
                if (a.PointsPerSecondWhenHiderHidden > 0) { a.PointsPerSecondWhenHiderHidden = -Math.Abs(a.PointsPerSecondWhenHiderHidden); changed = true; }

                // Бонус за поимку — неотрицательный
                if (a.CatchBonus < 0) { a.CatchBonus = Math.Abs(a.CatchBonus); changed = true; }

                return changed;
            }

            bool NormalizeHider(AgentConfig a)
            {
                bool changed = false;

                // Быть видимым должно штрафоваться
                if (a.RewardWhenVisible > 0) { a.RewardWhenVisible = -Math.Abs(a.RewardWhenVisible); changed = true; }
                if (a.PointsPerSecondWhenVisible > 0) { a.PointsPerSecondWhenVisible = -Math.Abs(a.PointsPerSecondWhenVisible); changed = true; }
                if (a.RewardWhenSeenBySeeker > 0) { a.RewardWhenSeenBySeeker = -Math.Abs(a.RewardWhenSeenBySeeker); changed = true; }

                // Быть скрытым должно поощряться
                if (a.RewardWhenHidden < 0) { a.RewardWhenHidden = Math.Abs(a.RewardWhenHidden); changed = true; }
                if (a.PointsPerSecondWhenHidden < 0) { a.PointsPerSecondWhenHidden = Math.Abs(a.PointsPerSecondWhenHidden); changed = true; }

                // Позитивные бонусы
                if (a.EscapeBonus < 0) { a.EscapeBonus = Math.Abs(a.EscapeBonus); changed = true; }
                if (a.RewardWhenIncreasingDistance < 0) { a.RewardWhenIncreasingDistance = Math.Abs(a.RewardWhenIncreasingDistance); changed = true; }
                if (a.RewardWhenHiddenBehindWall < 0) { a.RewardWhenHiddenBehindWall = Math.Abs(a.RewardWhenHiddenBehindWall); changed = true; }

                return changed;
            }
        }

        /// <summary>
        /// Validate configuration; returns a string array of errors (empty if valid).
        /// </summary>
        public string[] Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (World.GridSize <= 0) errors.Add("World.GridSize must be > 0.");
            if (World.CellSize <= 0) errors.Add("World.CellSize must be > 0.");
            if (World.WallHeight <= 0) errors.Add("World.WallHeight must be > 0.");
            if (World.RoomSize <= 0) errors.Add("World.RoomSize must be > 0.");

            if (SessionDurationSeconds <= 0) errors.Add("SessionDurationSeconds must be > 0.");
            if (FramesForCatch <= 0) errors.Add("FramesForCatch must be > 0.");
            if (ActionRepeat <= 0) errors.Add("ActionRepeat must be > 0.");
            if (VisibilityCheckInterval <= 0) errors.Add("VisibilityCheckInterval must be > 0.");
            if (NoProgressDistanceEps < 0) errors.Add("NoProgressDistanceEps must be >= 0.");
            if (NoProgressSeconds < 0) errors.Add("NoProgressSeconds must be >= 0.");
            if (MinInitialSeparation < 0) errors.Add("MinInitialSeparation must be >= 0.");
            if (TimeScale <= 0) errors.Add("TimeScale must be > 0.");

            // Sub-config validations
            errors.AddRange(Training.Validate());
            errors.AddRange(Model.Validate());
            errors.AddRange(ReplayBuffer.Validate());
            if (Actions == null) errors.Add("Actions (ActionSpaceConfig) must not be null.");
            else errors.AddRange(Actions.Validate());

            return errors.ToArray();
        }

        /// <summary>
        /// Builds an effective DQNConfig by mapping values from the new structured sections
        /// (Model, Training, ReplayBuffer). This allows legacy components expecting DQNConfig
        /// to work without reading legacy DQN section from config files.
        /// </summary>
        public DQNConfig BuildEffectiveDqnConfig()
        {
            var d = new DQNConfig
            {
                // Architecture
                Hidden1 = Model.Hidden1,
                Hidden2 = Model.Hidden2,

                // Core RL
                Gamma = Model.Gamma,

                // Epsilon-greedy
                EpsilonStart = Model.EpsilonStart,
                EpsilonMin = Model.EpsilonMin,
                EpsilonDecay = Model.EpsilonDecay,

                // Training loop
                BatchSize = Training.BatchSize,
                StepsPerUpdate = Training.StepsPerUpdate,

                // Replay buffer
                ReplayBufferSize = ReplayBuffer.Size,
                WarmupSize = ReplayBuffer.WarmupSize,

                // Optimizer/Loss
                LearningRate = Model.LearningRate,
                UseHuberLoss = Model.UseHuberLoss,
                MaxGradNorm = Model.MaxGradNorm,
                UseAdamW = Model.UseAdamW,
                WeightDecay = Model.WeightDecay,

                // Target net
                UpdateTargetEvery = Model.UpdateTargetEvery,
                UseSoftTarget = Model.UseSoftTarget,
                TargetUpdateTau = Model.TargetUpdateTau,

                // Reward processing
                RewardClipAbs = Model.RewardClipAbs,
                RewardScale = Model.RewardScale,

                // PER / sampling
                BetaStart = ReplayBuffer.BetaStart,
                BetaEnd = ReplayBuffer.BetaEnd,
                BetaFrames = ReplayBuffer.BetaFrames,
                UseStratifiedSampling = ReplayBuffer.UseStratifiedSampling,
            };
            return d;
        }
    }

    /// <summary>
    /// Параметры генерации мира (размер сетки, размер клетки, высота стен, размер комнаты).
    /// </summary>
    public class WorldConfig
    {
        /// <summary> Размер сетки (NxN клеток). </summary>
        public int GridSize { get; set; } = 20;
        /// <summary> Размер одной клетки (юниты Raylib). </summary>
        public float CellSize { get; set; } = 1.0f;
        /// <summary> Высота стены. </summary>
        public float WallHeight { get; set; } = 2.0f;
        /// <summary> Размер "комнаты" — минимальное расстояние между внутренними стенами. </summary>
        public int RoomSize { get; set; } = 8;
    }

    /// <summary>
    /// Параметры наград, поведения и характеристик агента (Seeker или Hider).
    /// </summary>
    public class AgentConfig
    {
        // === Базовые награды ===
        /// <summary> Награда Seeker, если Hider видим. </summary>
        public float RewardWhenHiderVisible { get; set; } = 1.0f;
        /// <summary> Награда Seeker, если Hider скрыт. </summary>
        public float RewardWhenHiderHidden { get; set; } = -0.05f;
        /// <summary> Награда Hider, если его видно. </summary>
        public float RewardWhenVisible { get; set; } = -0.6f;
        /// <summary> Награда Hider, если его не видно. </summary>
        public float RewardWhenHidden { get; set; } = 0.15f;

        // === Дополнительные награды для Hider ===
        /// <summary> Награда Hider, если его видит Seeker. </summary>
        public float RewardWhenSeenBySeeker { get; set; } = -0.5f;
        /// <summary> Награда Hider за увеличение расстояния. </summary>
        public float RewardWhenIncreasingDistance { get; set; } = 0.2f;
        /// <summary> Награда Hider за прятки за стеной. </summary>
        public float RewardWhenHiddenBehindWall { get; set; } = 0.15f;

        // === Бонусы и баллы ===
        /// <summary> Сколько баллов в секунду за нахождение Hider. </summary>
        public float PointsPerSecondWhenHiderVisible { get; set; } = 6.0f;
        /// <summary> Сколько баллов в секунду, если Hider скрыт. </summary>
        public float PointsPerSecondWhenHiderHidden { get; set; } = -0.2f;
        /// <summary> Баллы Hider за то, что его видно. </summary>
        public float PointsPerSecondWhenVisible { get; set; } = -1.0f;
        /// <summary> Баллы Hider за то, что скрыт. </summary>
        public float PointsPerSecondWhenHidden { get; set; } = 1.0f;
        /// <summary> Бонус Seeker за поимку Hider. </summary>
        public float CatchBonus { get; set; } = 30.0f;
        /// <summary> Бонус Hider за побег. </summary>
        public float EscapeBonus { get; set; } = 2.0f;

        // === Параметры агента ===
        /// <summary> Бонус за физическое исследование клеток. </summary>
        public float PhysicalExploreReward { get; set; } = 0.05f;
        /// <summary> Бонус за визуальное исследование клеток. </summary>
        public float VisualExploreReward { get; set; } = 0.01f;
        /// <summary> Радиус зрения агента. </summary>
        public float VisionRadius { get; set; } = 6.0f;
        /// <summary> Угол зрения агента (градусы). </summary>
        public float VisionAngle { get; set; } = 90.0f;
        /// <summary> Радиус агента (юниты Raylib). </summary>
        public float AgentRadius { get; set; } = 0.3f;
        /// <summary> Скорость агента. </summary>
        public float Speed { get; set; } = 2.0f;
        /// <summary> Шаг поворота при дискретных действиях (градусы). </summary>
        public float RotationStepDegrees { get; set; } = 10.0f;

        /// <summary> Количество агентов данной роли. </summary>
        public int Count { get; set; } = 2;

        // === Новые параметры для гибкой настройки поведения ===
        /// <summary> Множитель награды за близость к цели. </summary>
        public float ProximityRewardMultiplier { get; set; } = 0.1f;
        /// <summary> Штраф за повороты. </summary>
        public float RotationPenalty { get; set; } = 0.01f;
        /// <summary> Штраф за отсутствие прогресса. </summary>
        public float NoProgressPenalty { get; set; } = 0.02f;

        /// <summary>
        /// Если true, агент (для Hider) при том, что его видят, действует жадно на этот шаг
        /// (игнорирует epsilon-exploration), чтобы удерживать или увеличивать дистанцию.
        /// </summary>
        public bool ForceExploitWhenSeen { get; set; } = true;

        /// <summary>
        /// Включить добавочный shaping награды на стороне агента: если Hider находится в луче/конусе видимости Seeker,
        /// к базовой награде добавляется RewardWhenSeenBySeeker (знак/величину задаёт конфиг).
        /// </summary>
        public bool ApplyVisibilityShapingInAgent { get; set; } = true;
    }

    /// <summary>
    /// Параметры DQN-агента (архитектура сети, обучающие параметры и буфер).
    /// </summary>
    public class DQNConfig
    {
        /// <summary> Кол-во нейронов в 1 скрытом слое. </summary>
        public int Hidden1 { get; set; } = 256;
        /// <summary> Кол-во нейронов во 2 скрытом слое. </summary>
        public int Hidden2 { get; set; } = 256;
        /// <summary> Коэффициент дисконтирования будущей награды. </summary>
        public float Gamma { get; set; } = 0.99f;

        // Epsilon-greedy
        /// <summary> Начальное значение epsilon для epsilon-greedy. </summary>
        public float EpsilonStart { get; set; } = 1.0f;
        /// <summary> Минимальное значение epsilon. </summary>
        public float EpsilonMin { get; set; } = 0.05f;
        /// <summary> Коэффициент затухания epsilon (мультипликативный). </summary>
        public float EpsilonDecay { get; set; } = 0.995f;

        // Обучение/буфер
        /// <summary> Размер batch для обучения. </summary>
        public int BatchSize { get; set; } = 128;
        /// <summary> Размер буфера опыта. </summary>
        public int ReplayBufferSize { get; set; } = 20000;
        /// <summary> Минимальное наполнение буфера перед обучением. </summary>
        public int WarmupSize { get; set; } = 1280; // ~10*b
        /// <summary> Количество шагов обучения на один шаг окружения. </summary>
        public int StepsPerUpdate { get; set; } = 2;

        // Оптимизатор/лосс
        /// <summary> Скорость обучения (learning rate). </summary>
        public float LearningRate { get; set; } = 0.0005f;
        /// <summary> Использовать Huber (SmoothL1) вместо MSE. </summary>
        public bool UseHuberLoss { get; set; } = true;
        /// <summary> Клиппинг нормы градиента. 0 — отключено. </summary>
        public float MaxGradNorm { get; set; } = 10.0f;
        /// <summary> Использовать AdamW вместо Adam. </summary>
        public bool UseAdamW { get; set; } = true;
        /// <summary> Weight decay для AdamW. </summary>
        public float WeightDecay { get; set; } = 0.0001f;

        // Target network
        /// <summary> Частота жесткого обновления целевой сети. </summary>
        public int UpdateTargetEvery { get; set; } = 200;
        /// <summary> Использовать мягкое обновление (Polyak). </summary>
        public bool UseSoftTarget { get; set; } = true;
        /// <summary> Коэффициент Polyak-обновления. </summary>
        public float TargetUpdateTau { get; set; } = 0.005f;

        // Награды
        /// <summary> Клиппинг абсолютного значения награды (0 — отключено). </summary>
        public float RewardClipAbs { get; set; } = 1.0f;
        /// <summary> Масштабирование награды после клиппинга. </summary>
        public float RewardScale { get; set; } = 1.0f;

        // PER
        /// <summary> Начальное значение beta для PER IS-весов. </summary>
        public float BetaStart { get; set; } = 0.4f;
        /// <summary> Конечное значение beta для PER IS-весов. </summary>
        public float BetaEnd { get; set; } = 1.0f;
        /// <summary> За сколько обучающих шагов дорастить beta до BetaEnd. </summary>
        public int BetaFrames { get; set; } = 100000;
        /// <summary> Использовать стратифицированную выборку из PER. </summary>
        public bool UseStratifiedSampling { get; set; } = true;

/// <summary>
/// Вычисляет текущее значение beta на шаге обучения step:
/// линейная интерполяция от BetaStart к BetaEnd за BetaFrames шагов с насыщением.
/// </summary>
public float GetBetaAtStep(int step)
{
    if (BetaFrames <= 0) return BetaEnd;
    if (step <= 0) return BetaStart;
    if (step >= BetaFrames) return BetaEnd;

    float t = (float)step / BetaFrames;
    t = Math.Clamp(t, 0f, 1f);
    return BetaStart + (BetaEnd - BetaStart) * t;
}
    }
}
