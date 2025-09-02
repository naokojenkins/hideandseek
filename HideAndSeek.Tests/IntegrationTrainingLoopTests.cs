using System;
using System.Linq;
using TorchSharp;
using HideAndSeek.Core.RL;
using Xunit;

namespace HideAndSeek.Tests
{
    public class IntegrationTrainingLoopTests
    {
        public sealed class FakeModule
        {
            private readonly torch.Tensor[] _ps;
            public FakeModule(params torch.Tensor[] ps) { _ps = ps; }
            public System.Collections.Generic.IEnumerable<torch.Tensor> parameters() => _ps;
        }

        [Fact]
        public void Minimal_Training_Stub_Buffer_Scheduler_Loss_TargetUpdater()
        {
            // Setup buffer with simple experiences
            var rng = new Random(42);
            var rb = new PrioritizedReplayBuffer(capacity: 32, alpha: 0.6f, rng: rng);
            for (int i = 0; i < 16; i++)
            {
                var exp = new Experience(
                    state: new float[] { i },
                    action: i % 3,
                    reward: (float)(i * 0.1),
                    nextState: new float[] { i + 1 },
                    done: i % 5 == 0);
                rb.Add(exp, error: (float)(Math.Abs(Math.Sin(i)) + 0.01));
            }

            var scheduler = new LinearBetaScheduler(0.4f, 1.0f, 10);
            float beta = scheduler.GetBeta(learnStep: 5);
            var batch = rb.Sample(batchSize: 8, beta: beta, stratified: true);

            // Form q-values and targets (dummy): q = indices, target = indices + 1
            using var q = torch.tensor(batch.Indices.Select(i => (float)i).ToArray());
            using var t = torch.tensor(batch.Indices.Select(i => (float)i + 1f).ToArray());

            var lossCalc = new MSELossCalculator();
            using var lossVec = lossCalc.Calculate(q, t);
            Assert.Equal(new long[] { (long)batch.Indices.Length }, lossVec.shape); // 1D vector
            Assert.True(torch.all(lossVec >= 0).ToBoolean());
            Assert.True(torch.all(lossVec.isfinite()).ToBoolean());

            // Update priorities based on absolute TD error (sqrt of MSE per element is abs error)
            using var absErr = (q - t).abs();
            var newErrors = absErr.data<float>().ToArray();
            rb.UpdatePriorities(batch.Indices, newErrors);

            // Apply soft target update to fake models
            using var src = torch.tensor(new float[] { 2f, 2f });
            using var dst = torch.tensor(new float[] { 0f, 0f });
            var model = new FakeModule(src);
            var target = new FakeModule(dst);
            var updater = new SoftTargetUpdater(0.5f);
            updater.Update(model, target, step: 6);
            Assert.True(torch.allclose(dst, torch.tensor(new float[] { 1f, 1f }), rtol:1e-6, atol:1e-6));
        }
    }
}
