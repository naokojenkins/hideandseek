using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HideAndSeek.Core.RL;
using Xunit;

namespace HideAndSeek.Tests
{
    public class ReplayBufferTests
    {
        private static PrioritizedReplayBuffer MakeBufferWithPriorities(params float[] errors)
        {
            var rb = new PrioritizedReplayBuffer(capacity: 100, alpha: 0.6f, rng: new Random(123));
            foreach (var e in errors)
            {
                var exp = new Experience(
                    state: new float[] { 0f },
                    action: 0,
                    reward: 0f,
                    nextState: new float[] { 0f },
                    done: false
                );
                rb.Add(exp, e);
            }
            return rb;
        }

        [Fact]
        public void Sample_Empty_Throws()
        {
            var rb = new PrioritizedReplayBuffer(10);
            Assert.Throws<InvalidOperationException>(() => rb.Sample(4, beta: 0.4f, stratified: false));
        }

        [Fact]
        public void Sample_BatchIsClamped_WhenInsufficient()
        {
            var rb = MakeBufferWithPriorities(1, 1, 1);
            var batch = rb.Sample(10, beta: 0.5f, stratified: false);
            Assert.Equal(3, batch.Actions.Length);
            Assert.Equal(3, batch.Weights.Length);
            Assert.Equal(3, batch.Indices.Length);
        }

        [Fact]
        public void UpdatePriorities_HandlesInvalidIndices_NoThrow()
        {
            var rb = MakeBufferWithPriorities(1, 2, 3);
            var ex = Record.Exception(() => rb.UpdatePriorities(new[] { -1, 0, 100 }, new[] { 0.1f, 0.2f, 0.3f }));
            Assert.Null(ex);
        }

        [Fact]
        public void ISWeights_MaxEqualsOne()
        {
            var rb = MakeBufferWithPriorities(0.1f, 1f, 2f, 3f, 4f, 5f);
            var batch = rb.Sample(6, beta: 1.0f, stratified: true);
            float maxW = batch.Weights.Max();
            Assert.InRange(maxW, 0.99999f, 1.00001f);
        }

        [Fact]
        public void ISWeights_MonotonicVsProbabilities_WithinBatch()
        {
            // Construct buffer with distinct priorities so probabilities differ
            var rb = MakeBufferWithPriorities(0.1f, 0.2f, 0.4f, 0.8f, 1.6f, 3.2f, 6.4f, 12.8f);
            var batch = rb.Sample(8, beta: 1.0f, stratified: false);

            // Reflect to get private buffer and priorities to compute probabilities
            var bufferField = typeof(PrioritizedReplayBuffer).GetField("buffer", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(bufferField);
            var internalList = (System.Collections.IEnumerable)bufferField!.GetValue(rb)!;

            // Extract priorities via reflection: each item has property Priority
            var itemType = internalList.GetType().GetGenericArguments().First();
            var priorityProp = itemType.GetProperty("Priority")!;

            var priorities = new List<float>();
            foreach (var item in internalList)
            {
                priorities.Add((float)priorityProp.GetValue(item)!);
            }
            float sum = priorities.Sum();
            var probs = priorities.Select(p => p / sum).ToArray();

            // For any two sampled indices i,j: if p_i > p_j + eps, then w_i < w_j + tol (inverse monotonic)
            var idxs = batch.Indices;
            var ws = batch.Weights;
            const float eps = 1e-8f;
            const float tol = 1e-5f;
            for (int a = 0; a < idxs.Length; a++)
            for (int b = 0; b < idxs.Length; b++)
            {
                if (probs[idxs[a]] > probs[idxs[b]] + eps)
                {
                    Assert.True(ws[a] < ws[b] + tol);
                }
            }
        }

        [Fact]
        public void Sample_Stratifed_Flag_AcceptsBothModes()
        {
            var rb = MakeBufferWithPriorities(1, 2, 3, 4, 5);
            var batchS = rb.Sample(5, beta: 0.4f, stratified: true);
            var batchI = rb.Sample(5, beta: 0.4f, stratified: false);
            // both modes should produce valid indices in range and non-empty weights
            Assert.All(batchS.Indices, idx => Assert.InRange(idx, 0, rb.Count - 1));
            Assert.All(batchI.Indices, idx => Assert.InRange(idx, 0, rb.Count - 1));
            Assert.Equal(5, batchS.Weights.Length);
            Assert.Equal(5, batchI.Weights.Length);
        }
    }
}
