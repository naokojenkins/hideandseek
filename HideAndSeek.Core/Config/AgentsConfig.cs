using System.IO;
using HideAndSeek.Core.IO;
using Newtonsoft.Json;

namespace HideAndSeek.Core.Config
{
    public class AgentsConfig
    {
        public static string FileName { get; set; } = "agents_config.json";

        public AgentConfig Seeker { get; set; } = new AgentConfig();
        public AgentConfig Hider { get; set; } = new AgentConfig();

        public static AgentsConfig Load(string? explicitPath = null)
        {
            string path = explicitPath ?? PathService.GetConfigPath(FileName);
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<AgentsConfig>(json) ?? new AgentsConfig();
                    return cfg;
                }
            }
            catch { }
            return new AgentsConfig();
        }
    }
}
