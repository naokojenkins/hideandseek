using System;
using System.Collections.Generic;
using TorchSharp;
using Xunit;
using HideAndSeek.Core.RL;

namespace HideAndSeek.Tests
{
    public class SoftTargetUpdaterTests
    {
        public sealed class FakeModule
        {
            private readonly List<torch.Tensor> _parameters;
            public FakeModule(params torch.Tensor[] ps) { _parameters = new List<torch.Tensor>(ps); }
            public IEnumerable<torch.Tensor> parameters() => _parameters;
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-0.1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void Ctor_InvalidTau_Throws(float tau)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoftTargetUpdater(tau));
        }

        [Fact]
        public void Update_Blends_Towards_Model()
        {
            var tau = 0.5f;
            var updater = new SoftTargetUpdater(tau);

            using var mW = torch.tensor(new float[] { 2f, 2f, 2f });
            using var tW = torch.tensor(new float[] { 0f, 0f, 0f });

            var model = new FakeModule(mW);
            var target = new FakeModule(tW);

            updater.Update(model, target, step: 0);

            // new target should be 0*(1-tau) + 2*tau = 1
            Assert.True(torch.allclose(tW, torch.tensor(new float[] { 1f, 1f, 1f }), rtol:1e-6, atol:1e-6));
        }

        [Fact]
        public void Update_MultipleParameters_AllBlended()
        {
            var tau = 1.0f; // full copy
            var updater = new SoftTargetUpdater(tau);

            using var mW1 = torch.tensor(new float[] { 3f });
            using var mW2 = torch.tensor(new float[] { -1f, 4f });
            using var tW1 = torch.tensor(new float[] { 0f });
            using var tW2 = torch.tensor(new float[] { 0f, 0f });

            var model = new FakeModule(mW1, mW2);
            var target = new FakeModule(tW1, tW2);

            updater.Update(model, target, step: 10);

            Assert.True(torch.allclose(tW1, mW1));
            Assert.True(torch.allclose(tW2, mW2));
        }
    }
}
