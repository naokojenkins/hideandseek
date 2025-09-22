using System;
using System.Numerics;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.RaylibThreeD;
using Xunit;

namespace HideAndSeek.Tests
{
    public class NavigationMemoryTests
    {
        private World3D MakeWorld(int size = 10)
        {
            return new World3D(size);
        }

        [Fact]
        public void Seeker_NoLOS_WithFreshOpponentMemory_GoesTowardLastPosition()
        {
            var world = MakeWorld(10);
            var seeker = new Agent3D(new Vector3(5, 0, 5), isSeeker: true, initialRotation: 0f);
            seeker.SetWorld(world);
            seeker.InitWorldSize(world.Size);
            seeker.Id = "S1";

            // Last known opponent at +X (angle ~0)
            seeker.Memory.ReportSeen("H1", MemoryKind.Opponent, new Vector3(8, 0, 5), 0f, time: 0f);
            // No decay so confidence is 1
            float? best = seeker.GetBestDirection(world, lookaheadDistance: 2f);
            Assert.True(best.HasValue);
            // Expect near 0 +/- 30 among candidates
            Assert.True(Math.Abs(((best.Value + 360f) % 360f) - 0f) <= 30f || Math.Abs(best.Value - 360f) <= 30f);
        }

        [Fact]
        public void Hider_NoLOS_WithFreshSeekerMemory_GoesAwayFromLastPosition()
        {
            var world = MakeWorld(10);
            var hider = new Agent3D(new Vector3(5, 0, 5), isSeeker: false, initialRotation: 0f);
            hider.SetWorld(world);
            hider.InitWorldSize(world.Size);
            hider.Id = "H1";

            // Last known opponent (seeker) at +X, hider should go to ~180
            hider.Memory.ReportSeen("S1", MemoryKind.Opponent, new Vector3(8, 0, 5), 0f, time: 0f);
            float? best = hider.GetBestDirection(world, lookaheadDistance: 2f);
            Assert.True(best.HasValue);
            // Expect near 180 within 30 deg
            float diff = Math.Abs(((best.Value + 360f) % 360f) - 180f);
            diff = Math.Min(diff, 360f - diff);
            Assert.True(diff <= 30f);
        }

        [Fact]
        public void AllyRepulsion_ShiftsDirectionAwayFromAllyCluster()
        {
            var world = MakeWorld(10);
            var agent = new Agent3D(new Vector3(5, 0, 5), isSeeker: true, initialRotation: 0f);
            agent.SetWorld(world);
            agent.InitWorldSize(world.Size);
            agent.Id = "S1";

            // Two allies ahead at +X direction
            agent.Memory.ReportSeen("S2", MemoryKind.Ally, new Vector3(7, 0, 5), 0f, time: 0f);
            agent.Memory.ReportSeen("S3", MemoryKind.Ally, new Vector3(8, 0, 5), 0f, time: 0f);

            // No opponent target, so exploration prefers 0 deg, but repulsion should push away -> favor turning
            float? best = agent.GetBestDirection(world, lookaheadDistance: 2f);
            Assert.True(best.HasValue);
            // It should not strictly prefer 0; often 180 or side due to repulsion; ensure it's not 0
            Assert.NotEqual(0f, best.Value);
        }
    }
}
