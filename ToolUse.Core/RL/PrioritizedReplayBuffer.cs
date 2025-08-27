using System;
using System.Collections.Generic;

namespace ToolUse.Core.RL
{
    /// <summary>
    /// A simple Prioritized Experience Replay (PER) buffer with proportional prioritization.
    /// </summary>
    /// <remarks>
    /// Priorities are computed as (|error| + epsilon)^alpha. Sampling probability is priority / sum(priority).
    /// Sample returns importance-sampling (IS) weights normalized so that max(weight) == 1 within the batch.
    /// </remarks>
    public class PrioritizedReplayBuffer : IReplayBuffer, IEnumerable<Experience>
    {
        public const float DefaultAlpha = 0.6f;
        public const float DefaultEpsilon = 1e-6f;
        private const float MinTotalPriority = 1e-6f;
        private const float MinProbability = 1e-8f;
        private const float MaxBetaClamp = 10f;

        private class PrioritizedExperience
        {
            public Experience Experience { get; set; }
            public float Priority { get; set; }
        }

        private readonly int _capacity;
        private readonly List<PrioritizedExperience> _buffer = new();
                // Back-compat: legacy tests reflect on a private field named 'buffer'. Keep alias pointing to the same list.
                private readonly List<PrioritizedExperience> buffer;
        private readonly float _alpha = DefaultAlpha;
        private readonly float _epsilon = DefaultEpsilon;
        private readonly Random _random;

        /// <summary>
        /// Creates a new PER buffer.
        /// </summary>
        /// <param name="capacity">Max number of items stored. Oldest items are evicted when capacity is exceeded.</param>
        /// <param name="alpha">Priority exponent controlling how strongly sampling favors high-error items.</param>
        /// <param name="rng">Optional RNG for sampling.</param>
        public PrioritizedReplayBuffer(int capacity, float alpha = DefaultAlpha, Random? rng = null)
        {
            _capacity = capacity;
            _alpha = alpha;
            _random = rng ?? new Random();
            buffer = _buffer; // back-compat alias initialization
            // Preallocate list capacity to minimize resizing/memory fragmentation
            try { _buffer.Capacity = Math.Max(_buffer.Capacity, capacity); } catch { /* ignore if capacity invalid */ }
        }

        /// <inheritdoc />
        public int Count => _buffer.Count;

        /// <inheritdoc />
        public void Add(Experience exp, float error = 1.0f)
        {
            _buffer.Add(new PrioritizedExperience
            {
                Experience = exp,
                Priority = (float)Math.Pow(Math.Abs(error) + _epsilon, _alpha)
            });

            if (_buffer.Count > _capacity)
                _buffer.RemoveAt(0);
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

        /// <inheritdoc />
        public (float[][] States, long[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones, float[] Weights, int[] Indices)
            Sample(int batchSize, float beta, bool stratified)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize), "Sample: batchSize must be > 0");
            if (_buffer.Count == 0) throw new InvalidOperationException("Sample: buffer is empty");
            if (batchSize > _buffer.Count) batchSize = _buffer.Count; // clamp to available size
            if (float.IsNaN(beta) || float.IsInfinity(beta)) throw new ArgumentOutOfRangeException(nameof(beta), "Sample: beta must be finite");
            beta = Math.Clamp(beta, 0f, MaxBetaClamp); // typical range [0,1], but allow up to 10 as defensive

            // Compute total priority without LINQ allocations
            float totalPriority = 0f;
            for (int i = 0; i < _buffer.Count; i++) totalPriority += _buffer[i].Priority;
            if (totalPriority <= 0f) totalPriority = MinTotalPriority;

            // Build CDF directly from normalized priorities, avoiding probabilities[] allocation
            int n = _buffer.Count;
            var cdf = new float[n];
            float cum = 0f;
            for (int i = 0; i < n; i++)
            {
                cum += _buffer[i].Priority / totalPriority;
                cdf[i] = cum;
            }
            if (n == 0 || cdf[n - 1] <= 0f) throw new InvalidOperationException("Sample: invalid CDF generated");

            // Draw indices
            var indices = new int[batchSize];
            if (stratified)
            {
                for (int i = 0; i < batchSize; i++)
                {
                    float u0 = i / (float)batchSize;
                    float u1 = (i + 1) / (float)batchSize;
                    float u = u0 + (float)_random.NextDouble() * (u1 - u0);
                    indices[i] = FindIndexInCdf(cdf, u);
                }
            }
            else
            {
                for (int i = 0; i < batchSize; i++)
                {
                    float u = (float)_random.NextDouble();
                    indices[i] = FindIndexInCdf(cdf, u);
                }
            }

            // Importance-sampling weights, normalized so that max == 1 in the batch
            int N = n;
            var weights = new float[batchSize];
            float maxWeight = 0f;
            for (int i = 0; i < batchSize; i++)
            {
                int idx = indices[i];
                if ((uint)idx >= (uint)n) throw new IndexOutOfRangeException($"Sample: sampled index {idx} out of range [0,{n})");
                float p = _buffer[idx].Priority / totalPriority;
                if (p < MinProbability) p = MinProbability;
                float w = (float)Math.Pow(N * p, -beta);
                weights[i] = w;
                if (w > maxWeight && float.IsFinite(w)) maxWeight = w;
            }
            if (maxWeight <= 0f || float.IsNaN(maxWeight) || float.IsInfinity(maxWeight)) maxWeight = 1f;
            for (int i = 0; i < batchSize; i++)
            {
                float w = weights[i];
                weights[i] = (float.IsFinite(w) && w > 0f) ? (w / maxWeight) : 1f;
            }

            // Materialize outputs without LINQ
            var states = new float[batchSize][];
            var actions = new long[batchSize];
            var rewards = new float[batchSize];
            var nextStates = new float[batchSize][];
            var dones = new bool[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                var e = _buffer[indices[i]].Experience;
                states[i] = e.State;
                actions[i] = e.Action;
                rewards[i] = e.Reward;
                nextStates[i] = e.NextState;
                dones[i] = e.Done;
            }

            return (
                states,
                actions,
                rewards,
                nextStates,
                dones,
                weights,
                indices
            );
        }

        public System.Collections.Generic.IEnumerator<Experience> GetEnumerator()
        {
            // Avoid multiple LINQ enumerators. Simple manual enumerator is fine given infrequent use.
            foreach (var item in _buffer) yield return item.Experience;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Clear() => _buffer.Clear();
        public List<Experience> ToList()
        {
            var list = new List<Experience>(_buffer.Count);
            for (int i = 0; i < _buffer.Count; i++) list.Add(_buffer[i].Experience);
            return list;
        }

        /// <inheritdoc />
        public void UpdatePriorities(int[] indices, float[] errors)
        {
            if (indices is null) throw new ArgumentNullException(nameof(indices));
            if (errors is null) throw new ArgumentNullException(nameof(errors));
            if (indices.Length != errors.Length) throw new ArgumentException("UpdatePriorities: indices and errors length mismatch");
            for (int i = 0; i < indices.Length; i++)
            {
                int idx = indices[i];
                if (idx < 0 || idx >= _buffer.Count)
                {
                    // Log and skip invalid indices
                    System.Console.WriteLine($"[WARN][PER] UpdatePriorities: index {idx} is out of bounds [0,{_buffer.Count}). Skipping.");
                    continue;
                }
                float error = errors[i];
                if (float.IsNaN(error) || float.IsInfinity(error)) error = 0f;
                _buffer[idx].Priority = (float)Math.Pow(Math.Abs(error) + _epsilon, _alpha);
            }
        }
    }
}
