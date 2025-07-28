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
        public float RewardWhenHiderVisible { get; set; } = 1.0f;
        public float RewardWhenHiderHidden { get; set; } = -0.01f;
        public float RewardWhenVisible { get; set; } = -0.5f;
        public float RewardWhenHidden { get; set; } = 0.1f;
        public float ExplorationBonusPerCell { get; set; } = 0.5f;
        public float ExplorationScoreMultiplier { get; set; } = 1.0f;
        public float PointsPerSecondWhenHiderVisible { get; set; } = 10.0f;
        public float PointsPerSecondWhenHiderHidden { get; set; } = 0.1f;
        public float PointsPerSecondWhenVisible { get; set; } = -1.0f;
        public float PointsPerSecondWhenHidden { get; set; } = 1.0f;
        public bool ProximityRewardEnabled { get; set; } = true;
        public float MaxProximityDistance { get; set; } = 10.0f;
        public float ProximityRewardMultiplier { get; set; } = 2.0f;
        public bool MovementRewardEnabled { get; set; } = true;
        public float MovementRewardPerSecond { get; set; } = 0.1f;
        public bool IdlePenaltyEnabled { get; set; } = true;
        public float IdlePenaltyPerSecond { get; set; } = -0.05f;
        public bool DistanceRewardEnabled { get; set; } = true;
        public float MinSafeDistance { get; set; } = 5.0f;
        public float DistanceRewardMultiplier { get; set; } = 1.0f;

        // Новые параметры (используются в Agent3D)
        public float VisionRadius { get; set; } = 8.0f;
        public float VisionAngle { get; set; } = 90.0f;
        public float AgentRadius { get; set; } = 0.3f;
        public float Speed { get; set; } = 2.0f;
    }
}
