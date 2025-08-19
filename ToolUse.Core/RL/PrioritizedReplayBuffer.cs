using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolUse.Core.RL
{
    public class PrioritizedReplayBuffer : IReplayBuffer, IEnumerable<Experience>
    {
        private class PrioritizedExperience
        {
            public Experience Experience { get; set; }
            public float Priority { get; set; }
        }

        private readonly int capacity;
        private readonly List<PrioritizedExperience> buffer = new();
        private readonly float alpha = 0.6f;
        private readonly float epsilon = 1e-6f;
        private readonly Random rnd;

        public PrioritizedReplayBuffer(int capacity, float alpha = 0.6f, Random? rng = null)
        {
            this.capacity = capacity;
            this.alpha = alpha;
            this.rnd = rng ?? new Random();
        }

        public int Count => buffer.Count;

        public void Add(Experience exp, float error = 1.0f)
        {
            buffer.Add(new PrioritizedExperience
            {
                Experience = exp,
                Priority = (float)Math.Pow(Math.Abs(error) + epsilon, alpha)
            });

            if (buffer.Count > capacity)
                buffer.RemoveAt(0);
        }

        private static int FindIndexInCdf(float[] cdf, float u)
        {
            int lo = 0, hi = cdf.Length - 1, found = hi;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (u <= cdf[mid])
                {
                    found = mid;
                    hi = mid - 1;
                }
                else lo = mid + 1;
            }
            return found;
        }

        public (float[][] States, long[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones, float[] Weights, int[] Indices)
            Sample(int batchSize, float beta, bool stratified)
        {
            float totalPriority = buffer.Sum(x => x.Priority);
            if (totalPriority <= 0f) totalPriority = 1e-6f;

            float[] probabilities = buffer.Select(x => x.Priority / totalPriority).ToArray();
            var cdf = new float[probabilities.Length];
            float cum = 0f;
            for (int i = 0; i < probabilities.Length; i++)
            {
                cum += probabilities[i];
                cdf[i] = cum;
            }

            var indices = new List<int>(batchSize);

            if (stratified)
            {
                for (int i = 0; i < batchSize; i++)
                {
                    float u0 = i / (float)batchSize;
                    float u1 = (i + 1) / (float)batchSize;
                    float u = u0 + (float)rnd.NextDouble() * (u1 - u0);
                    indices.Add(FindIndexInCdf(cdf, u));
                }
            }
            else
            {
                for (int i = 0; i < batchSize; i++)
                {
                    float u = (float)rnd.NextDouble();
                    indices.Add(FindIndexInCdf(cdf, u));
                }
            }

            // Importance-sampling weights
            int N = buffer.Count;
            float[] weights = indices.Select(idx =>
            {
                float p = Math.Max(probabilities[idx], 1e-8f);
                return (float)Math.Pow(N * p, -beta);
            }).ToArray();

            float maxWeight = weights.Max();
            if (maxWeight <= 0f) maxWeight = 1f;
            weights = weights.Select(w => w / maxWeight).ToArray();

            return (
                indices.Select(i => buffer[i].Experience.State).ToArray(),
                indices.Select(i => buffer[i].Experience.Action).ToArray(),
                indices.Select(i => buffer[i].Experience.Reward).ToArray(),
                indices.Select(i => buffer[i].Experience.NextState).ToArray(),
                indices.Select(i => buffer[i].Experience.Done).ToArray(),
                weights,
                indices.ToArray()
            );
        }

        public IEnumerator<Experience> GetEnumerator() => buffer.Select(x => x.Experience).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => buffer.Select(x => x.Experience).GetEnumerator();
        public void Clear() => buffer.Clear();
        public List<Experience> ToList() => buffer.Select(x => x.Experience).ToList();

        public void UpdatePriorities(int[] indices, float[] errors)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                int idx = indices[i];
                float error = errors[i];
                buffer[idx].Priority = (float)Math.Pow(Math.Abs(error) + epsilon, alpha);
            }
        }
    }
}
