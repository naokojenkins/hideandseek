using HideAndSeek.Core.RL;
using Xunit;

namespace HideAndSeek.Tests
{
    public class LinearBetaSchedulerTests
    {
        [Fact]
        public void Returns_Start_And_End_With_Clamping()
        {
            var sch = new LinearBetaScheduler(0.4f, 1.0f, 100);
            Assert.Equal(0.4f, sch.GetBeta(-10));
            Assert.Equal(0.4f, sch.GetBeta(0));
            Assert.Equal(1.0f, sch.GetBeta(100));
            Assert.Equal(1.0f, sch.GetBeta(1000));
        }

        [Fact]
        public void Interpolates_Linearly_Monotonic_Increasing()
        {
            var sch = new LinearBetaScheduler(0.2f, 0.8f, 10);
            float prev = sch.GetBeta(0);
            for (int s = 1; s <= 10; s++)
            {
                float cur = sch.GetBeta(s);
                Assert.True(cur >= prev - 1e-6);
                prev = cur;
            }
            // midpoint around step=5 should be ~0.2 + 0.6 * 0.5 = 0.5 (t uses /frames, clamped)
            float mid = sch.GetBeta(5);
            Assert.InRange(mid, 0.5f - 1e-5f, 0.5f + 1e-5f);
        }

        [Fact]
        public void Interpolates_Linearly_Monotonic_Decreasing()
        {
            var sch = new LinearBetaScheduler(1.0f, 0.3f, 10);
            float prev = sch.GetBeta(0);
            for (int s = 1; s <= 10; s++)
            {
                float cur = sch.GetBeta(s);
                Assert.True(cur <= prev + 1e-6);
                prev = cur;
            }
            float mid = sch.GetBeta(5);
            Assert.InRange(mid, 0.65f - 1e-5f, 0.65f + 1e-5f);
        }
    }
}
