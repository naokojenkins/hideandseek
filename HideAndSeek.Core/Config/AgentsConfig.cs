using System;
using System.IO;
using HideAndSeek.Core.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HideAndSeek.Core.Config
{
    public class AgentsConfig
    {
        public static string FileName { get; set; } = "agents_config.json";

        public AgentConfig Seeker { get; set; } = new AgentConfig();
        public AgentConfig Hider { get; set; } = new AgentConfig();

        public static AgentsConfig Load(string? explicitPath = null)
        {
            // Resolution strategy:
            // 1) explicitPath if provided
            // 2) Sibling next to resolved GameConfig path (same directory as game_config.json)
            // 3) Generic search via PathService.GetConfigPath(FileName)
            string path = explicitPath ?? string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    // Try sibling to game config
                    try
                    {
                        string gamePath = PathService.GetConfigPath(GameConfig.ConfigPath ?? "game_config.json");
                        if (!string.IsNullOrWhiteSpace(gamePath) && File.Exists(gamePath))
                        {
                            string? dir = Path.GetDirectoryName(gamePath);
                            if (!string.IsNullOrWhiteSpace(dir))
                            {
                                string sibling = Path.Combine(dir!, FileName);
                                if (File.Exists(sibling)) path = sibling;
                            }
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    path = PathService.GetConfigPath(FileName);
                }

                // 2b) Try explicit repo path HideAndSeek.Sim/configs/agents_config.json if still not found or doesn't exist
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    try
                    {
                        string repoRelative = System.IO.Path.Combine("HideAndSeek.Sim", "configs", FileName);
                        string candidate = PathService.GetConfigPath(repoRelative);
                        if (File.Exists(candidate)) path = candidate;
                    }
                    catch { }
                }

                if (File.Exists(path))
                {
                    try { System.Console.WriteLine($"[CONFIG] AgentsConfig loaded from: {path}"); } catch { }
                    var json = File.ReadAllText(path);
                    // Parse minimally and manually to avoid exceptions from [JsonProperty(Required = Always)] on AgentConfig
                    var cfg = new AgentsConfig();
                    var root = JObject.Parse(json);

                    if (root["Seeker"] is JObject seekerObj)
                    {
                        if (seekerObj["Count"] != null) cfg.Seeker.Count = seekerObj.Value<int>("Count");
                        if (seekerObj["SeenByOpponentPenaltyPerStep"] != null) cfg.Seeker.SeenByOpponentPenaltyPerStep = seekerObj.Value<float>("SeenByOpponentPenaltyPerStep");
                        if (seekerObj["FleeDistanceRewardMultiplierWhenSeen"] != null) cfg.Seeker.FleeDistanceRewardMultiplierWhenSeen = seekerObj.Value<float>("FleeDistanceRewardMultiplierWhenSeen");
                    }

                    if (root["Hider"] is JObject hiderObj)
                    {
                        if (hiderObj["Count"] != null) cfg.Hider.Count = hiderObj.Value<int>("Count");
                        // Hider section may not define these; leave defaults if absent
                        if (hiderObj["SeenByOpponentPenaltyPerStep"] != null) cfg.Hider.SeenByOpponentPenaltyPerStep = hiderObj.Value<float>("SeenByOpponentPenaltyPerStep");
                        if (hiderObj["FleeDistanceRewardMultiplierWhenSeen"] != null) cfg.Hider.FleeDistanceRewardMultiplierWhenSeen = hiderObj.Value<float>("FleeDistanceRewardMultiplierWhenSeen");
                    }

                    return cfg;
                }
                else
                {
                    try { System.Console.WriteLine($"[CONFIG] AgentsConfig NOT FOUND. Tried path: {path}"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { System.Console.WriteLine($"[CONFIG] AgentsConfig load error: {ex.Message}"); } catch { }
                // Swallow and fallback to defaults to keep app running
            }
            try { System.Console.WriteLine("[CONFIG] Using default AgentsConfig (Seeker.Count=2, Hider.Count=2)"); } catch { }
            return new AgentsConfig();
        }
    }
}
