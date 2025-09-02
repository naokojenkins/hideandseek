using System;
using HideAndSeek.Core.RL;
using TorchSharp;
using Xunit;

namespace HideAndSeek.Tests
{
    public class DQNAgentGuardrailsTests
    {
        private static DQNAgent MakeAgent(int stateSize = 4, int actionSize = 3)
        {
            var cfg = new HideAndSeek.Core.Config.DQNConfig
            {
                Hidden1 = 8,
                Hidden2 = 8,
                Gamma = 0.99f,
                EpsilonStart = 1.0f,
                EpsilonMin = 0.05f,
                EpsilonDecay = 0.99f,
                BatchSize = 2,
                ReplayBufferSize = 16,
                WarmupSize = 2,
                StepsPerUpdate = 1,
                LearningRate = 0.0005f,
                UpdateTargetEvery = 10,
                UseSoftTarget = false,
            };
            // Use CPU device override to avoid CUDA init in tests
            return new DQNAgent(stateSize, actionSize, cfg, torch.CPU);
        }

        [Fact]
        public void Ctor_InvalidSizes_ShouldThrow()
        {
            var cfg = new HideAndSeek.Core.Config.DQNConfig();
            Assert.Throws<ArgumentOutOfRangeException>(() => new DQNAgent(0, 1, cfg));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DQNAgent(1, 0, cfg));
            Assert.Throws<ArgumentNullException>(() => new DQNAgent(1, 1, (HideAndSeek.Core.Config.DQNConfig)null!));
        }

        [Fact]
        public void SetExternalContext_Null_ShouldThrow()
        {
            var agent = MakeAgent();
            Assert.Throws<ArgumentNullException>(() => agent.SetExternalContext(null!));
        }

        [Fact]
        public void ChooseAction_StateNullOrWrongLength_ShouldThrow()
        {
            var agent = MakeAgent(stateSize: 3, actionSize: 2);
            Assert.Throws<ArgumentNullException>(() => agent.ChooseAction(null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => agent.ChooseAction(new float[] { 1f }));
        }

        [Fact]
        public void Store_InvalidArgs_ShouldThrow()
        {
            var agent = MakeAgent(stateSize: 3, actionSize: 2);
            var s = new float[] { 0f, 0f, 0f };
            var ns = new float[] { 0f, 0f, 0f };
            Assert.Throws<ArgumentNullException>(() => agent.Store(null!, 0, 0f, ns, false));
            Assert.Throws<ArgumentNullException>(() => agent.Store(s, 0, 0f, null!, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => agent.Store(new float[] { 0f }, 0, 0f, ns, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => agent.Store(s, 0, 0f, new float[] { 0f }, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => agent.Store(s, -1, 0f, ns, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => agent.Store(s, 2, 0f, ns, false)); // actionSize=2 -> valid 0..1
        }
    }
}
