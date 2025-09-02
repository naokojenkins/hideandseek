using System;
using TorchSharp;
using static TorchSharp.torch;

namespace HideAndSeek.Core.RL
{
    /// <summary>
    /// Utilities to efficiently convert managed jagged arrays (float[][]) into contiguous torch tensors.
    /// Avoids per-element boxing/allocations by copying rows into a single preallocated buffer.
    /// </summary>
    public static class BatchTensorizer
    {
        /// <summary>
        /// Converts a 2D jagged array (batch x features) to a contiguous float32 tensor on the specified device.
        /// </summary>
        /// <param name="data">Jagged array with consistent inner lengths.</param>
        /// <param name="device">Target torch device.</param>
        /// <param name="requiresGrad">Whether the created tensor requires gradients.</param>
        public static torch.Tensor ToTensor2D(float[][] data, torch.Device device, bool requiresGrad = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            int batch = data.Length;
            if (batch == 0) return zeros(new long[] {0, 0}, dtype: ScalarType.Float32, device: device);
            int features = data[0]?.Length ?? 0;
            if (features <= 0) throw new ArgumentException("Inner arrays must be non-null and non-empty", nameof(data));
            // Validate consistent inner length
            for (int i = 1; i < batch; i++)
            {
                if (data[i] == null || data[i].Length != features)
                    throw new ArgumentException("All inner arrays must be non-null and have the same length", nameof(data));
            }

            var flat = new float[batch * features];
            int offset = 0;
            for (int i = 0; i < batch; i++)
            {
                var row = data[i];
                Array.Copy(row, 0, flat, offset, features);
                offset += features;
            }

            var t = torch.tensor(flat, new long[] { batch, features }, dtype: ScalarType.Float32, device: device);
            if (requiresGrad) t.requires_grad_();
            return t;
        }

        /// <summary>
        /// Converts a 1D float array to a float32 tensor on device.
        /// </summary>
        public static torch.Tensor ToTensor1D(float[] data, torch.Device device, bool requiresGrad = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var t = torch.tensor(data, new long[] { data.Length }, dtype: ScalarType.Float32, device: device);
            if (requiresGrad) t.requires_grad_();
            return t;
        }

        /// <summary>
        /// Converts a 1D long array to a int64 tensor on device.
        /// </summary>
        public static torch.Tensor ToTensor1D(long[] data, torch.Device device)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return torch.tensor(data, new long[] { data.Length }, dtype: ScalarType.Int64, device: device);
        }

        /// <summary>
        /// Converts a 1D bool array to a float32 mask tensor on device (true->1f, false->0f).
        /// </summary>
        public static torch.Tensor ToMask1D(bool[] data, torch.Device device)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            int n = data.Length;
            var tmp = new float[n];
            for (int i = 0; i < n; i++) tmp[i] = data[i] ? 1f : 0f;
            return torch.tensor(tmp, new long[] { n }, dtype: ScalarType.Float32, device: device);
        }
    }
}
