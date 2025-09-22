using System;
using System.Linq;
using System.Numerics;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.RaylibThreeD;
using Xunit;

namespace HideAndSeek.Tests
{
    public class AgentMemoryTests
    {
        [Fact]
        public void ReportSeen_ThenDecayAndPurge_WorkAsExpected()
        {
            // Ensure defaults
            var cfg = GameConfig.Instance; // loads defaults if not loaded
            var mem = new AgentMemory();

            mem.ReportSeen("X", MemoryKind.Opponent, new Vector3(1,0,1), 90f, time: 0f);
            Assert.True(mem.TryGetLastOpponent(out var e));
            Assert.Equal("X", e.TargetId);
            Assert.Equal(1.0f, e.Confidence, 3);

            // After 2 seconds at 0.25/s -> 0.5
            mem.Decay(2f);
            Assert.True(mem.TryGetLastOpponent(out e));
            Assert.InRange(e.Confidence, 0.49f, 0.51f);

            // After MaxAgeSeconds + eps -> removed
            float now = GameConfig.Instance.Memory.MaxAgeSeconds + 0.1f;
            mem.Decay(now);
            Assert.False(mem.TryGetLastOpponent(out _));
        }
    }
}
