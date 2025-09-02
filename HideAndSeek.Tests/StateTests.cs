using System;
using HideAndSeek.Core.RL;
using Xunit;

namespace HideAndSeek.Tests
{
    public class StateTests
    {
        [Fact]
        public void FromString_Legacy_NoSeen_NoWalls()
        {
            var s = "ax=1,ay=2,ox=3,oy=4,dir=5,see=true";
            var st = State.FromString(s);
            Assert.Equal(1, st.AgentX);
            Assert.Equal(2, st.AgentY);
            Assert.Equal(3, st.OtherX);
            Assert.Equal(4, st.OtherY);
            Assert.Equal(5, st.Direction);
            Assert.True(st.CanSee);
            Assert.False(st.IsSeenBySeeker);
            Assert.Empty(st.KnownWallsFlat);
        }

        [Fact]
        public void FromString_New_WithSeen_And_Walls()
        {
            var s = "ax=0,ay=0,ox=7,oy=8,dir=90,see=false,seen=true,walls=1010";
            var st = State.FromString(s);
            Assert.Equal(0, st.AgentX);
            Assert.Equal(0, st.AgentY);
            Assert.Equal(7, st.OtherX);
            Assert.Equal(8, st.OtherY);
            Assert.Equal(90, st.Direction);
            Assert.False(st.CanSee);
            Assert.True(st.IsSeenBySeeker);
            Assert.Equal(new[] { true, false, true, false }, st.KnownWallsFlat);
        }

        [Fact]
        public void FromString_Invalid_Prefix_ShouldDescribe()
        {
            var s = "ax=1,ay=2,ox=3,oy=4,dirX=5,see=true";
            var ex = Assert.Throws<FormatException>(() => State.FromString(s));
            Assert.Contains("Expected token 'dir=...'", ex.Message);
        }

        [Fact]
        public void FromString_Invalid_Walls_Char()
        {
            var s = "ax=1,ay=2,ox=3,oy=4,dir=5,see=true,seen=false,walls=10a1";
            var ex = Assert.Throws<FormatException>(() => State.FromString(s));
            Assert.Contains("Invalid character 'a' at walls[2]", ex.Message);
        }

        [Fact]
        public void ToArray_WorldSize1_Ok()
        {
            var st = new State(1, 2, 3, 4, 90, true, new bool[] { true }, false);
            var arr = st.ToArray(1);
            // 7 basic + 1 wall
            Assert.Equal(8, arr.Length);
            // Positions normalized by worldSize=1 -> just the raw ints
            Assert.Equal(1f, arr[0]);
            Assert.Equal(2f, arr[1]);
            Assert.Equal(3f, arr[2]);
            Assert.Equal(4f, arr[3]);
            // Direction 90 deg -> sector 2 -> 2/8=0.25
            Assert.InRange(arr[4], 0.2499f, 0.2501f);
            Assert.Equal(1f, arr[5]);
            Assert.Equal(0f, arr[6]);
            Assert.Equal(1f, arr[7]);
        }

        [Fact]
        public void ToArray_WorldSize0_ShouldThrow()
        {
            var st = new State(0, 0, 0, 0, 0, false, Array.Empty<bool>(), false);
            Assert.Throws<ArgumentOutOfRangeException>(() => st.ToArray(0));
        }

        [Fact]
        public void ToArray_KnownWalls_Shorter_ShouldThrow()
        {
            var walls = new bool[5]; // expected 9 for world=3
            var st = new State(0, 0, 0, 0, 0, false, walls, false);
            var ex = Assert.Throws<ArgumentException>(() => st.ToArray(3));
            Assert.Contains("shorter", ex.Message);
        }

        [Fact]
        public void ToArray_KnownWalls_Longer_ShouldThrow()
        {
            var walls = new bool[10]; // expected 9 for world=3
            var st = new State(0, 0, 0, 0, 0, false, walls, false);
            var ex = Assert.Throws<ArgumentException>(() => st.ToArray(3));
            Assert.Contains("longer", ex.Message);
        }

        [Fact]
        public void Json_RoundTrip_Version1()
        {
            var st = new State(5, 6, 7, 8, 270, true, new[] { false, true, false, true }, true);
            var json = st.ToJson();
            var back = State.FromJson(json);
            Assert.Equal(st, back);
        }

        [Fact]
        public void Json_UnknownVersion_ShouldThrow()
        {
            // manually craft different version
            var json = "{\"version\":2,\"ax\":0,\"ay\":0,\"ox\":0,\"oy\":0,\"dir\":0,\"see\":false,\"seen\":false}";
            Assert.Throws<NotSupportedException>(() => State.FromJson(json));
        }
    }
}
