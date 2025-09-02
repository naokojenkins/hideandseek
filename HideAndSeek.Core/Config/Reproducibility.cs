using System;
using System.Threading;
using TorchSharp;

namespace HideAndSeek.Core.Config
{
    /// <summary>
    /// Centralized reproducibility/seeding helper.
    /// - Holds an effective global seed (from configuration)
    /// - Seeds TorchSharp CPU/CUDA RNGs
    /// - Provides deterministic Random instances via sub-seeds
    /// </summary>
    public static class Reproducibility
    {
        private static int _effectiveSeed = 0;
        private static long _counter = 0;
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        /// <summary>
        /// The effective global seed used for all RNGs in the process.
        /// </summary>
        public static int EffectiveSeed => _effectiveSeed;

        /// <summary>
        /// Initialize global seeding. Safe to call multiple times; subsequent calls with the same seed are no-ops.
        /// </summary>
        public static void Initialize(int? seedNullable)
        {
            int seed = seedNullable ?? GameConfig.Instance.Seed;
            if (seed == 0) seed = 12345; // Avoid 0 if someone passes it as "unset"

            lock (_lock)
            {
                if (_initialized && _effectiveSeed == seed)
                    return;

                _effectiveSeed = seed;
                _counter = 0;

                // Seed TorchSharp RNGs
                try
                {
                    TorchSharp.torch.random.manual_seed((long)seed);
                    if (TorchSharp.torch.cuda.is_available())
                    {
                        TorchSharp.torch.cuda.manual_seed_all((long)seed);
                    }
                }
                catch { /* TorchSharp may not be fully initialized on some environments, ignore */ }

                _initialized = true;
            }
        }

        /// <summary>
        /// Create a deterministic sub-seed derived from the effective seed. Thread-safe.
        /// </summary>
        public static int NextSubSeed()
        {
            lock (_lock)
            {
                // SplitMix64 derivation
                ulong z = (ulong)_effectiveSeed + ((ulong)++_counter * 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= (z >> 31);
                // keep non-negative 31-bit int for Random
                return unchecked((int)(z & 0x7FFFFFFF));
            }
        }

        /// <summary>
        /// Creates a new System.Random seeded deterministically from the global seed.
        /// Tag is unused but can be helpful for future logging/tracing.
        /// </summary>
        public static Random CreateRandom(string? tag = null)
        {
            var subSeed = NextSubSeed();
            return new Random(subSeed);
        }
    }
}
