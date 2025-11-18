using System;
using System.IO;
using Newtonsoft.Json;

namespace HideAndSeek.Core.Config
{
    /// <summary>
    /// Простая RGBA-структура для задания цветов через конфиг.
    /// </summary>
    public record ColorConfig(byte R, byte G, byte B, byte A = 255)
    {
        public Raylib_cs.Color ToRaylibColor() => new Raylib_cs.Color(R, G, B, A);
    }

    /// <summary>
    /// Главный конфиг всей симуляции: параметры мира, агентов, DQN и наград.
    /// Используйте GameConfig.Instance для доступа к единому объекту конфигурации во всём проекте!
    /// </summary>
    public class GameConfig
    {
        private static GameConfig? _instance;
        /// <summary> Schema version for game_config.json to allow migrations. </summary>
        public int Version { get; set; } = 3;

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

        // New runtime/physics/logging sections
        public RuntimeConfig Runtime { get; set; } = new RuntimeConfig();
        public PhysicsConfig Physics { get; set; } = new PhysicsConfig();
        public LoggingConfig Logging { get; set; } = new LoggingConfig();

        /// <summary>
        /// Параметры памяти агентов (индвидуальные наблюдения и веса навигации).
        /// </summary>
        public MemoryConfig Memory { get; set; } = new MemoryConfig();

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
        /// Эффективный порог кадров для поимки (>=1). Вычисляется на лету как функция базового
        /// FramesForCatch с учётом ActionRepeat и TimeScale, чтобы поддерживать корректное поведение
        /// при рантайм-изменении этих параметров.
        ///
        /// Правила пересчёта:
        /// - Базовый FramesForCatch трактуется как порог при ActionRepeat=1 и TimeScale=1.
        /// - При увеличении ActionRepeat эпизод ускоряется в терминах «решений на единицу времени»,
        ///   поэтому для сохранения сопоставимой длительности удержания цели в поле зрения порог
        ///   умножается на ActionRepeat.
        /// - При изменении TimeScale масштабируется длительность сим-шагов: при TimeScale>1 на один
        ///   кадр приходится больше сим-времени, следовательно требуемое число кадров уменьшается
        ///   пропорционально (деление на TimeScale). Для TimeScale<1 — наоборот.
        /// </summary>
        [JsonIgnore]
        public int EffectiveFramesForCatch
        {
            get
            {
                int baseFrames = Math.Max(1, FramesForCatch);
                int repeat = Math.Max(1, ActionRepeat);
                // Do NOT adjust by TimeScale here: SimulationApp already applies time scaling to deltaTime.
                // Catch logic counts frames, not seconds, so only ActionRepeat should affect the threshold.
                double scaled = Math.Ceiling(baseFrames * (double)repeat);
                if (scaled > int.MaxValue) return int.MaxValue;
                return Math.Max(1, (int)scaled);
            }
        }

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
        /// <summary> Максимальное число попыток подбора стартовых позиций (чтобы соблюсти разделение). </summary>
        public int InitialPlacementMaxAttempts { get; set; } = 200;
        /// <summary> Коэффициент сжатия времени (1 — без сжатия). </summary>
        public float TimeScale { get; set; } = 1.0f;

        /// <summary>
        /// Приватный загрузчик (используйте только через Instance).
        /// </summary>
        private static GameConfig Load(string? path = null)
        {
            // Resolve path via PathService to also support reading configs from source tree without rebuild
            string resolvedPath = HideAndSeek.Core.IO.PathService.GetConfigPath(path ?? ConfigPath);

            try
            {
                if (File.Exists(resolvedPath))
                {
                    using var fs = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sr = new StreamReader(fs);
                    string json = sr.ReadToEnd();
                    var cfg = JsonConvert.DeserializeObject<GameConfig>(json) ?? new GameConfig();
                    MigrateIfNeeded(cfg);
                    // Overlay per-role agent settings from agents_config.json if present to ensure uniform usage across the app
                    try
                    {
                        var agents = AgentsConfig.Load();
                        if (agents != null)
                        {
                            cfg.Seeker = agents.Seeker ?? cfg.Seeker;
                            cfg.Hider = agents.Hider ?? cfg.Hider;
                        }
                    }
                    catch { }
                    NormalizeConfig(cfg);
                    return cfg;
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Config file not found: {resolvedPath}, using defaults");
                    var cfg = new GameConfig();
                    // Try overlay agents from agents_config.json even when main config is missing
                    try
                    {
                        var agents = AgentsConfig.Load();
                        if (agents != null)
                        {
                            cfg.Seeker = agents.Seeker ?? cfg.Seeker;
                            cfg.Hider = agents.Hider ?? cfg.Hider;
                        }
                    }
                    catch { }
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

                // v3: introduce explicit control for applying visibility rewards to RL vs HUD.
                if (cfg.Version < 3)
                {
                    // Preserve legacy behavior: Hider used visibility rewards in RL, Seeker did not.
                    cfg.Hider.ApplyVisibilityRewardsToRL = true;
                    // Keep default scales (1.0) as defined in AgentConfig.
                    cfg.Version = 3;
                    Console.WriteLine("[CONFIG] Migration complete (schema v3): ApplyVisibilityRewardsToRL defaults applied (Hider=true, Seeker=false).");
                }

                // v4: introduce Runtime/Physics/Logging sections with defaults (FPS, epsilon, log rotation)
                if (cfg.Version < 4)
                {
                    cfg.Runtime ??= new RuntimeConfig();
                    cfg.Physics ??= new PhysicsConfig();
                    cfg.Logging ??= new LoggingConfig();
                    if (cfg.Runtime.FpsVisual <= 0) cfg.Runtime.FpsVisual = 40;
                    if (cfg.Runtime.FpsHeadless <= 0) cfg.Runtime.FpsHeadless = 60;
                    if (cfg.Physics.MinNeighborDistanceEps <= 0) cfg.Physics.MinNeighborDistanceEps = 1e-5f;
                    if (cfg.Logging.RetainedFileCountLimit < 0) cfg.Logging.RetainedFileCountLimit = 7;
                    if (cfg.Logging.FlushToDiskIntervalSeconds <= 0) cfg.Logging.FlushToDiskIntervalSeconds = 3;
                    cfg.Version = 4;
                    Console.WriteLine("[CONFIG] Migration complete (schema v4): Runtime/Physics/Logging defaults applied.");
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

            if (SessionDurationSeconds <= 0) errors.Add("SessionDurationSeconds must be > 0.");
            if (FramesForCatch <= 0) errors.Add("FramesForCatch must be > 0.");
            if (ActionRepeat <= 0) errors.Add("ActionRepeat must be > 0.");
            if (VisibilityCheckInterval <= 0) errors.Add("VisibilityCheckInterval must be > 0.");
            if (NoProgressDistanceEps < 0) errors.Add("NoProgressDistanceEps must be >= 0.");
            if (NoProgressSeconds < 0) errors.Add("NoProgressSeconds must be >= 0.");
            if (MinInitialSeparation < 0) errors.Add("MinInitialSeparation must be >= 0.");
            if (InitialPlacementMaxAttempts <= 0) errors.Add("InitialPlacementMaxAttempts must be > 0.");
            if (TimeScale <= 0) errors.Add("TimeScale must be > 0.");

            // Sub-config validations
            errors.AddRange(Training.Validate());
            errors.AddRange(Model.Validate());
            errors.AddRange(ReplayBuffer.Validate());
            if (Actions == null) errors.Add("Actions (ActionSpaceConfig) must not be null.");
            else errors.AddRange(Actions.Validate());

            // Runtime/Physics/Logging validations
            if (Runtime == null) errors.Add("Runtime section must not be null.");
            else
            {
                if (Runtime.FpsVisual <= 0) errors.Add("Runtime.FpsVisual must be > 0.");
                if (Runtime.FpsHeadless <= 0) errors.Add("Runtime.FpsHeadless must be > 0.");
            }
            if (Physics == null) errors.Add("Physics section must not be null.");
            else
            {
                if (Physics.MinNeighborDistanceEps <= 0) errors.Add("Physics.MinNeighborDistanceEps must be > 0.");
            }
            if (Logging == null) errors.Add("Logging section must not be null.");
            else
            {
                if (Logging.RetainedFileCountLimit < 0) errors.Add("Logging.RetainedFileCountLimit must be >= 0.");
                if (Logging.FlushToDiskIntervalSeconds <= 0) errors.Add("Logging.FlushToDiskIntervalSeconds must be > 0.");
            }

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

        // Генерация/детерминизм
        /// <summary> Включать ли генерацию лабиринта (для больших миров). </summary>
        public bool UseMaze { get; set; } = true;
        /// <summary> Порог размера, начиная с которого применяется генерация лабиринта. </summary>
        public int MazeThresholdSize { get; set; } = 12;
        /// <summary> Seed мира (если null — недетерминированный, как раньше). </summary>
        public int? Seed { get; set; } = null;
        /// <summary> Тип генерации: "MazeDFS", "Empty", ... (на будущее). </summary>
        public string GenerationType { get; set; } = "MazeDFS";

        // Геометрические проверки
        /// <summary> Кол-во периметр-сэмплов при проверке окружности на пересечение со стенами. </summary>
        public int AreaFreePerimeterSamples { get; set; } = 24;
        /// <summary> Эпсилон смещения точки по радиусу к центру (чтобы избегать граничных эффектов). </summary>
        public float AreaFreeEdgeEpsilon { get; set; } = 0.999f;
        /// <summary> Шаг трассировки луча видимости (для fallback-реализации). </summary>
        public float LoSRaycastStep { get; set; } = 0.2f;
        /// <summary> Множитель бокового оффсета для толщины луча от радиуса агента. </summary>
        public float LoSRaycastSideOffsetFactor { get; set; } = 0.5f;

        // Визуализация мира
        public bool DrawGrid { get; set; } = true;
        public float GridY { get; set; } = 0.01f;
        public ColorConfig GridColor { get; set; } = new ColorConfig(60, 60, 60, 255);
        public ColorConfig FloorColor { get; set; } = new ColorConfig(200, 200, 200, 255);
        public ColorConfig WallColor { get; set; } = new ColorConfig(80, 80, 80, 255);
        public ColorConfig WallWireColor { get; set; } = new ColorConfig(0, 0, 0, 255);
        public bool DrawShadows { get; set; } = true;
        public float ShadowScale { get; set; } = 1.1f;
        public float ShadowHeight { get; set; } = 0.1f;
        public float ShadowBrightness { get; set; } = 0.5f;
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

        /// <summary>
        /// ВКЛ/ВЫКЛ: должны ли награды, связанные с видимостью (Visible/Hidden/SeenBySeeker), участвовать в RL-награде.
        /// Это снимает неоднозначность между HUD-«очками» и RL-наградой.
        /// По умолчанию: для Seeker — false (видимость влияет только на очки HUD), для Hider — true (сохраняем текущее поведение).
        /// </summary>
        public bool ApplyVisibilityRewardsToRL { get; set; } = false;
        /// <summary>
        /// Масштаб для видимых/скрытых RL-наград (если ApplyVisibilityRewardsToRL=true). Позволяет ослабить/усилить вклад.
        /// </summary>
        public float VisibilityRewardScaleRL { get; set; } = 1.0f;
        /// <summary>
        /// Масштаб для HUD-очков, связанных с видимостью. Не влияет на RL-награду.
        /// </summary>
        public float VisibilityPointsScaleHUD { get; set; } = 1.0f;
        /// <summary>
        /// Включает начисление HUD-очков за видимость/скрытность. Не влияет на RL.
        /// </summary>
        public bool EnableHudVisibilityPoints { get; set; } = true;

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
        /// <summary>
        /// ЕДИНЫЙ пер‑клеточный бонус за исследование новой клетки.
        /// Историческое имя свойства — «PhysicalExploreReward», но бонус применяется ко ВСЕМ видам «открытия» клетки:
        /// 1) когда клетка впервые попала в поле зрения (визуальное открытие),
        /// 2) когда клетка впервые пройдена/достигнута физически.
        /// Т.е. это один общий ExploreRewardPerCell; переименование не выполнено ради обратной совместимости конфигов.
        /// </summary>
        public float PhysicalExploreReward { get; set; } = 0.05f;
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
        /// <summary>
        /// Коэффициент выравнивания поворота для эвристики «беги от противника» (устарело).
        /// Если AlignThresholdDegrees <= 0, используется поведение по умолчанию: |diff| < RotationStepDegrees * TurnAlignFactor.
        /// Сохранено для обратной совместимости конфигов.
        /// </summary>
        public float TurnAlignFactor { get; set; } = 0.6f;

        /// <summary>
        /// Явный порог выравнивания по углу (в градусах) для выбора Forward vs ForwardLeft/Right в эвристике.
        /// Если > 0, имеет приоритет над TurnAlignFactor.
        /// Рекомендуется задавать фиксированное значение для воспроизводимости при разных RotationStepDegrees.
        /// Пример: при шаге 10° и старом коэффициенте 0.6 — 6.0°.
        /// Значение 0 или отрицательное означает использовать TurnAlignFactor.
        /// </summary>
        public float AlignThresholdDegrees { get; set; } = 0.0f;

        /// <summary> Количество агентов данной роли. </summary>
        public int Count { get; set; } = 2;

        // Визуализация и параметры чувствительности/семплинга обзора
        /// <summary>
        /// Цвет агента. Если не задан (null) — используются значения по умолчанию: 
        /// Seeker: (0,121,241), Hider: (0,228,48).
        /// </summary>
        public ColorConfig? AgentColor { get; set; } = null;
        public int VisionSegments { get; set; } = 60;
        public float VisionRayStep { get; set; } = 0.2f;
        public float MoveLookaheadFactor { get; set; } = 2.0f;
        public float MoveLookaheadMin { get; set; } = 0.6f;

        // === Новые параметры для гибкой настройки поведения ===
        /// <summary> Множитель награды за близость к цели. </summary>
        public float ProximityRewardMultiplier { get; set; } = 0.1f;
        /// <summary> Базовый штраф за поворот (применяется только если поворот не увеличил видимую/исследованную область).</summary>
        public float RotationPenalty { get; set; } = 0.01f;
        /// <summary>
        /// Коэффициент ослабления штрафа за поворот при побеге (когда агент виден противнику / убегает).
        /// Применяется как: EffectivePenalty = RotationPenalty * RotationPenaltyWhenFleeFactor.
        /// Рекомендуемый диапазон [0, 1]. Значение 0.3 соответствует прежнему захардкоженному поведению.
        /// </summary>
        public float RotationPenaltyWhenFleeFactor { get; set; } = 0.3f;
        /// <summary> БАЗОВЫЙ штраф за отсутствие прогресса (для обратной совместимости). </summary>
        public float NoProgressPenalty { get; set; } = 0.02f;
        /// <summary> Шаг штрафа за отсутствие прогресса (накапливается до лимита).</summary>
        public float NoProgressPenaltyStep { get; set; } = 0.02f;
        /// <summary> Максимальный накопленный штраф за отсутствие прогресса.</summary>
        public float NoProgressPenaltyMax { get; set; } = 0.2f;

        /// <summary>
        /// Если true, агент (и Hider, и Seeker — в пределах своей роли) при том, что его видят/он в угрозе,
        /// действует жадно на этот шаг (игнорирует epsilon-exploration). Для Hider — когда его видят, для Seeker — когда его видит противник.
        /// </summary>
        public bool ForceExploitWhenSeen { get; set; } = true;

        /// <summary>
        /// Включаемость эвристики «беги от угрозы» (для обеих ролей). При включении в шаге угрозы добавляется
        /// направляющая эвристика выравнивания и движения от источника угрозы.
        /// </summary>
        public bool EnableFleeHeuristic { get; set; } = true;

        /// <summary>
        /// Предпочтение «поворот+движение» (ForwardLeft/ForwardRight) vs чистый поворот (TurnLeft/TurnRight)
        /// при выравнивании направления в эвристике. Если true — используем поворот+движение, иначе — чистый поворот.
        /// </summary>
        public bool PreferTurnAndMoveWhenAligning { get; set; } = true;

        /// <summary>
        /// Включить добавочный shaping награды на стороне агента: если Hider находится в луче/конусе видимости Seeker,
        /// к базовой награде добавляется RewardWhenSeenBySeeker (знак/величину задаёт конфиг).
        /// </summary>
        public bool ApplyVisibilityShapingInAgent { get; set; } = true;

        // === Новые параметры для поиска и shaping ===
        /// <summary> Разовый бонус Seeker за первое обнаружение Hider в эпизоде (false→true в текущем шаге). Делится между увидевшими. </summary>
        public float DetectBonus { get; set; } = 0.0f;
        /// <summary> Если true, использовать potential-based shaping для Seeker: r += d - gamma*d' (Φ=-d). </summary>
        public bool UsePotentialShaping { get; set; } = true;
        /// <summary> Минимальное значение epsilon во время фазы поиска (IsHiderSeen=false) для Seeker. </summary>
        public float EpsilonWhenSearching { get; set; } = 0.6f;
        /// <summary> Доля смешивания Q с эвристическим приоритетом действий в фазе поиска (Q'=(1-α)Q+αP).</summary>
        public float HeuristicAlphaSearch { get; set; } = 0.2f;

        // === Видимость/преследование: параметры, заменяющие хардкоды ===
        /// <summary>
        /// Штраф Seeker за то, что его видит противник (в расчёте на шаг). Ранее был хардкод -0.05.
        /// Значение обычно отрицательное.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public float SeenByOpponentPenaltyPerStep { get; set; }
        /// <summary>
        /// Множитель вознаграждения за увеличение дистанции при побеге, когда Seeker сам находится под наблюдением.
        /// Ранее был хардкод 0.8.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public float FleeDistanceRewardMultiplierWhenSeen { get; set; }
    }

    /// <summary>
    /// Параметры DQN-агента (архитектура сети, обучающие параметры и буфер).
    /// </summary>
    public class MemoryConfig
    {
        // Lifetime/decay
        public float MaxAgeSeconds { get; set; } = 6.0f;
        public float DecayPerSecond { get; set; } = 0.25f;
        public float MinConfidenceForNav { get; set; } = 0.35f;

        // Repulsion/attraction radii
        public float AllyRepulsionRadius { get; set; } = 2.5f;
        public float SeekerOpponentAttractionRadius { get; set; } = 8.0f;
        public float HiderOpponentAvoidanceRadius { get; set; } = 8.0f;

        // Mixing weights
        public float SeekerW1_Target { get; set; } = 1.0f;
        public float SeekerW2_AllyRepulsion { get; set; } = 0.3f;
        public float SeekerW3_Exploration { get; set; } = 0.2f;
        public float HiderW1_Target { get; set; } = 1.0f;
        public float HiderW2_AllyRepulsion { get; set; } = 0.4f;
        public float HiderW3_Exploration { get; set; } = 0.3f;
    }

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


    public class RuntimeConfig
    {
        /// <summary> Target FPS when running with visualization. </summary>
        public int FpsVisual { get; set; } = 40;
        /// <summary> Target FPS when running headless. </summary>
        public int FpsHeadless { get; set; } = 60;
    }

    public class PhysicsConfig
    {
        /// <summary>
        /// Minimal neighbor distance epsilon used to avoid unstable behavior when measuring very small distances.
        /// </summary>
        public float MinNeighborDistanceEps { get; set; } = 1e-5f;
    }

    public class LoggingConfig
    {
        /// <summary> How many rolling log files to retain. </summary>
        public int RetainedFileCountLimit { get; set; } = 7;
        /// <summary> Flush interval to disk for file sink (seconds). </summary>
        public int FlushToDiskIntervalSeconds { get; set; } = 3;
    }
