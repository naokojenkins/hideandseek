using System;
using HideAndSeek.Core.Config;
using Xunit;

namespace HideAndSeek.Tests
{
    public class ActionSpaceConfigTests
    {
        [Fact]
        public void Validate_Default_IsValid()
        {
            var a = new ActionSpaceConfig();
            var errs = a.Validate();
            Assert.Empty(errs);
        }

        [Fact]
        public void Validate_DuplicateIndices_ReturnsError()
        {
            var a = new ActionSpaceConfig
            {
                TurnLeft = 0,
                TurnRight = 0, // duplicate
                Forward = 2,
                ForwardLeft = 3,
                ForwardRight = 4,
                Idle = 5,
                Backward = 6,
                Count = 7
            };
            var errs = a.Validate();
            Assert.Contains(errs, e => e.Contains("Duplicate action indices"));
        }

        [Fact]
        public void Validate_CountTooSmall_ReturnsError()
        {
            var a = new ActionSpaceConfig
            {
                TurnLeft = 0,
                TurnRight = 1,
                Forward = 2,
                ForwardLeft = 3,
                ForwardRight = 4,
                Idle = 5,
                Backward = 6,
                Count = 6 // should be at least 7
            };
            var errs = a.Validate();
            Assert.Contains(errs, e => e.Contains("Count must be >= maxIndex+1"));
        }

        [Fact]
        public void Validate_NegativeIndex_ReturnsError()
        {
            var a = new ActionSpaceConfig
            {
                TurnLeft = -1,
                TurnRight = 1,
                Forward = 2,
                ForwardLeft = 3,
                ForwardRight = 4,
                Idle = 5,
                Backward = 6,
                Count = 7
            };
            var errs = a.Validate();
            Assert.Contains(errs, e => e.Contains("non-negative"));
        }
    }
}
