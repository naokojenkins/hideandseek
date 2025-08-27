using System.Collections.Generic;

namespace ToolUse.Core.RL
{
    /// <summary>
    /// Contract for experience replay buffers used by RL agents.
    /// </summary>
    public interface IReplayBuffer
    {
        /// <summary>
        /// Gets the current number of items stored in the buffer.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Adds an experience to the buffer with an optional TD-error magnitude used to derive initial priority.
        /// </summary>
        /// <param name="exp">Experience tuple to store.</param>
        /// <param name="error">Absolute TD error estimate used to compute priority. Implementations may apply smoothing (epsilon) and exponent <c>alpha</c>.</param>
        void Add(Experience exp, float error = 1.0f);

        /// <summary>
        /// Samples a mini-batch from the buffer.
        /// </summary>
        /// <param name="batchSize">Requested batch size. Implementations may clamp to <see cref="Count"/> if insufficient samples exist.</param>
        /// <param name="beta">Importance-sampling exponent. 0 disables correction, 1 applies full correction.</param>
        /// <param name="stratified">If true, draws samples using stratified sampling over the CDF; otherwise i.i.d. draws.</param>
        /// <returns>
        /// A tuple containing arrays of states, actions, rewards, next states, terminal flags, importance-sampling weights, and the sampled buffer indices.
        /// </returns>
        /// <remarks>
        /// Invariants and semantics:
        /// - If <see cref="Count"/> is 0, implementations should throw an informative exception.
        /// - If <paramref name="batchSize"/> &gt; <see cref="Count"/>, implementations may either throw or clamp to <see cref="Count"/>; this implementation clamps.
        /// - Returned <c>Indices</c> are valid buffer indices for use with <see cref="UpdatePriorities(int[], float[])"/>.
        /// - Importance-sampling <c>Weights</c> must be normalized so that their maximum equals 1 within the batch.
        /// </remarks>
        (float[][] States, long[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones, float[] Weights, int[] Indices)
            Sample(int batchSize, float beta, bool stratified);

        /// <summary>
        /// Removes all items from the buffer.
        /// </summary>
        void Clear();

        /// <summary>
        /// Returns a copy of stored experiences in insertion order.
        /// </summary>
        List<Experience> ToList();

        /// <summary>
        /// Updates priorities of previously sampled items.
        /// </summary>
        /// <param name="indices">Buffer indices, typically those returned by <see cref="Sample(int,float,bool)"/>.</param>
        /// <param name="errors">New TD-error magnitudes aligned by position with <paramref name="indices"/>.</param>
        /// <remarks>
        /// Implementations must ignore out-of-bounds indices and may log a warning; no exception should be thrown for OOB.
        /// </remarks>
        void UpdatePriorities(int[] indices, float[] errors);
    }
}
