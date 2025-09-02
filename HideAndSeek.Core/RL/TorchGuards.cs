using System;
using TorchSharp;

namespace HideAndSeek.Core.RL
{
    /// <summary>
    /// Validation helpers for TorchSharp tensors to catch issues early.
    /// </summary>
    public static class TorchGuards
    {
        public static void EnsureSameShape(torch.Tensor a, torch.Tensor b, string context)
        {
            if (a is null) throw new ArgumentNullException(nameof(a), $"{context}: tensor 'a' is null");
            if (b is null) throw new ArgumentNullException(nameof(b), $"{context}: tensor 'b' is null");
            var ash = a.shape;
            var bsh = b.shape;
            if (ash.Length != bsh.Length)
                throw new ArgumentException($"{context}: shape rank mismatch: {a.shape} vs {b.shape}");
            for (int i = 0; i < ash.Length; i++)
            {
                if (ash[i] != bsh[i])
                    throw new ArgumentException($"{context}: shape mismatch at dim {i}: {ash[i]} != {bsh[i]} ({a.shape} vs {b.shape})");
            }
        }

        public static void EnsureFinite(torch.Tensor t, string context)
        {
            if (t is null) throw new ArgumentNullException(nameof(t), $"{context}: tensor is null");
            using var nanMask = t.isnan();
            using var infMask = t.isinf();
            using var nanAny = nanMask.any();
            using var infAny = infMask.any();
            bool hasNan = nanAny.ToBoolean();
            bool hasInf = infAny.ToBoolean();
            if (hasNan || hasInf)
            {
                throw new InvalidOperationException($"{context}: tensor contains {(hasNan ? "NaN" : "")} {(hasInf ? "Inf" : "").Trim()} values. Shape={t.shape}");
            }
        }
    }
}
