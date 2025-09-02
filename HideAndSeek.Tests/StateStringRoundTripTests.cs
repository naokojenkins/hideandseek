using HideAndSeek.Core.RL;
using Xunit;

namespace HideAndSeek.Tests
{
    public class StateStringRoundTripTests
    {
        [Fact]
        public void ToString_FromString_RoundTrip_Matches()
        {
            var walls = new[] { true, false, true, true, false };
            var s = new State(3, 4, 5, 6, 135, see: true, knownWalls: walls, isSeenBySeeker: true);
            var str = s.ToString();
            var back = State.FromString(str);
            Assert.Equal(s, back);
        }
    }
}
