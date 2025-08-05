using System;
using System.IO;
using Newtonsoft.Json;

namespace ToolUse.Core.Config
{
    public class GameConfig
    {
        public WorldConfig World { get; set; } = new WorldConfig();
        public AgentConfig Seeker { get; set; } = new AgentConfig();
        public AgentConfig Hider { get; set; } = new AgentConfig();
        public DQNConfig DQN { get; set; } = new DQNConfig();   // Новая секция!
        public float SessionDurationSeconds { get; set; } = 60f;
        public int FramesForCatch { get; set; } = 180;

        public static GameConfig Load(string path = "game_config.json")
        {
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

    public class WorldConfig
    {
        public int GridSize { get; set; } = 32;
        public float CellSize { get; set; } = 1.0f;
        public float WallHeight { get; set; } = 2.0f;
        public int RoomSize { get; set; } = 8;
    }

    public class AgentConfig
    {
        // === Базовые награды ===
        public float RewardWhenHiderVisible { get; set; } = 1.0f;
        public float RewardWhenHiderHidden { get; set; } = -0.01f;
        public float RewardWhenVisible { get; set; } = -0.5f;
        public float RewardWhenHidden { get; set; } = 0.1f;

        // === Дополнительные награды для Hider ===
        public float RewardWhenSeenBySeeker { get; set; } = 0.3f;            // ✅ Новое
        public float RewardWhenIncreasingDistance { get; set; } = 0.05f;      // ✅ Новое
        public float RewardWhenHiddenBehindWall { get; set; } = 0.15f;        // ✅ Новое

        // === Бонусы и баллы ===
        public float PointsPerSecondWhenHiderVisible { get; set; } = 10.0f;
        public float PointsPerSecondWhenHiderHidden { get; set; } = 0.1f;
        public float PointsPerSecondWhenVisible { get; set; } = -1.0f;
        public float PointsPerSecondWhenHidden { get; set; } = 1.0f;
        public float CatchBonus { get; set; } = 10.0f;
        public float EscapeBonus { get; set; } = 2.0f;

        // === Параметры агента ===
        public float PhysicalExploreReward { get; set; } = 0.05f;
        public float VisualExploreReward { get; set; } = 0.01f;
        public float VisionRadius { get; set; } = 8.0f;
        public float VisionAngle { get; set; } = 90.0f;
        public float AgentRadius { get; set; } = 0.3f;
        public float Speed { get; set; } = 2.0f;
    }

    // === Новая секция ===
    public class DQNConfig
    {
        public int Hidden1 { get; set; } = 256;          // Размер первого скрытого слоя
        public int Hidden2 { get; set; } = 256;          // Размер второго скрытого слоя
        public float Gamma { get; set; } = 0.99f;
        public float EpsilonStart { get; set; } = 1.0f;
        public float EpsilonMin { get; set; } = 0.05f;
        public float EpsilonDecay { get; set; } = 0.995f;
        public int BatchSize { get; set; } = 64;
        public int ReplayBufferSize { get; set; } = 10000;
        public float LearningRate { get; set; } = 0.0005f;
        public int UpdateTargetEvery { get; set; } = 200;
    }
}