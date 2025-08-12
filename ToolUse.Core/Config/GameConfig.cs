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
        /// </summary>
        public DQNConfig DQN { get; set; } = new DQNConfig();

        /// <summary>
        /// Длительность сессии (сек).
        /// </summary>
        public float SessionDurationSeconds { get; set; } = 60f;

        /// <summary>
        /// Сколько кадров подряд надо держать Hider в поле зрения для поимки.
        /// </summary>
        public int FramesForCatch { get; set; } = 180;

        /// <summary>
        /// Seed для всех случайных событий (Random) — используйте для повторяемости экспериментов!
        /// </summary>
        public int Seed { get; set; } = 12345;

        /// <summary>
        /// Приватный загрузчик (используйте только через Instance).
        /// </summary>
        private static GameConfig Load(string? path = null)
        {
            path ??= ConfigPath;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<GameConfig>(json) ?? new GameConfig();
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Config file not found: {path}, using defaults");
                    return new GameConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load config: {ex.Message}");
                return new GameConfig();
            }
        }
    }

    /// <summary>
    /// Параметры генерации мира (размер сетки, размер клетки, высота стен, размер комнаты).
    /// </summary>
    public class WorldConfig
    {
        /// <summary> Размер сетки (NxN клеток). </summary>
        public int GridSize { get; set; } = 32;
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
        public float RewardWhenHiderHidden { get; set; } = -0.01f;
        /// <summary> Награда Hider, если его видно. </summary>
        public float RewardWhenVisible { get; set; } = -0.5f;
        /// <summary> Награда Hider, если его не видно. </summary>
        public float RewardWhenHidden { get; set; } = 0.1f;

        // === Дополнительные награды для Hider ===
        /// <summary> Награда Hider, если его видит Seeker. </summary>
        public float RewardWhenSeenBySeeker { get; set; } = 0.3f;
        /// <summary> Награда Hider за увеличение расстояния. </summary>
        public float RewardWhenIncreasingDistance { get; set; } = 0.05f;
        /// <summary> Награда Hider за прятки за стеной. </summary>
        public float RewardWhenHiddenBehindWall { get; set; } = 0.15f;

        // === Бонусы и баллы ===
        /// <summary> Сколько баллов в секунду за нахождение Hider. </summary>
        public float PointsPerSecondWhenHiderVisible { get; set; } = 10.0f;
        /// <summary> Сколько баллов в секунду, если Hider скрыт. </summary>
        public float PointsPerSecondWhenHiderHidden { get; set; } = 0.1f;
        /// <summary> Баллы Hider за то, что его видно. </summary>
        public float PointsPerSecondWhenVisible { get; set; } = -1.0f;
        /// <summary> Баллы Hider за то, что скрыт. </summary>
        public float PointsPerSecondWhenHidden { get; set; } = 1.0f;
        /// <summary> Бонус Seeker за поимку Hider. </summary>
        public float CatchBonus { get; set; } = 10.0f;
        /// <summary> Бонус Hider за побег. </summary>
        public float EscapeBonus { get; set; } = 2.0f;

        // === Параметры агента ===
        /// <summary> Бонус за физическое исследование клеток. </summary>
        public float PhysicalExploreReward { get; set; } = 0.05f;
        /// <summary> Бонус за визуальное исследование клеток. </summary>
        public float VisualExploreReward { get; set; } = 0.01f;
        /// <summary> Радиус зрения агента. </summary>
        public float VisionRadius { get; set; } = 8.0f;
        /// <summary> Угол зрения агента (градусы). </summary>
        public float VisionAngle { get; set; } = 90.0f;
        /// <summary> Радиус агента (юниты Raylib). </summary>
        public float AgentRadius { get; set; } = 0.3f;
        /// <summary> Скорость агента. </summary>
        public float Speed { get; set; } = 2.0f;

        /// <summary> Количество агентов данной роли. </summary>
        public int Count { get; set; } = 1;

        // === Новые параметры для гибкой настройки поведения ===
        /// <summary> Множитель награды за близость к цели. </summary>
        public float ProximityRewardMultiplier { get; set; } = 0.1f;
        /// <summary> Штраф за повороты. </summary>
        public float RotationPenalty { get; set; } = 0.01f;
        /// <summary> Штраф за отсутствие прогресса. </summary>
        public float NoProgressPenalty { get; set; } = 0.02f;
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
        public int BatchSize { get; set; } = 64;
        /// <summary> Размер буфера опыта. </summary>
        public int ReplayBufferSize { get; set; } = 10000;
        /// <summary> Минимальное наполнение буфера перед обучением. </summary>
        public int WarmupSize { get; set; } = 640; // ~10*b
        /// <summary> Количество шагов обучения на один шаг окружения. </summary>
        public int StepsPerUpdate { get; set; } = 1;

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
    }
}
