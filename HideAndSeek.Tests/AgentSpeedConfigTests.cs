using System.Numerics;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.RaylibThreeD;
using Newtonsoft.Json;
using Xunit;

namespace HideAndSeek.Tests
{
    public class AgentSpeedConfigTests
    {
        [Fact]
        public void Agent3D_Constructed_UsesSpeedFromConfig_ForEachRole()
        {
            // Arrange: load base game config and overlay agents_config.json if present
            var baseCfg = GameConfig.Instance; // triggers load from game_config.json via PathService

            // Deep copy to avoid mutating global defaults of other tests
            var json = JsonConvert.SerializeObject(baseCfg);
            var merged = JsonConvert.DeserializeObject<GameConfig>(json) ?? new GameConfig();

            var agents = AgentsConfig.Load();
            if (agents != null)
            {
                merged.Seeker = agents.Seeker ?? merged.Seeker;
                merged.Hider = agents.Hider ?? merged.Hider;
            }

            GameConfig.SetInstance(merged);

            // Act: construct agents
            var seeker = new Agent3D(new Vector3(0, 0, 0), isSeeker: true);
            var hider  = new Agent3D(new Vector3(0, 0, 0), isSeeker: false);

            // Assert: speeds match config
            Assert.Equal(merged.Seeker.Speed, seeker.Speed, precision: 5);
            Assert.Equal(merged.Hider.Speed, hider.Speed, precision: 5);
        }
    }
}
