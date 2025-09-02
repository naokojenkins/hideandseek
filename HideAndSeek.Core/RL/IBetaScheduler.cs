namespace HideAndSeek.Core.RL
{
    /// <summary>
    /// Strategy interface to provide annealed beta values for PER importance-sampling weights.
    /// Implementations should be pure functions of the learning step and must be side-effect free.
    /// </summary>
    public interface IBetaScheduler
    {
        /// <summary>
        /// Get beta value at the given learning step. Implementations should clamp to a meaningful range, typically [0,1].
        /// </summary>
        /// <param name="learnStep">Number of completed learning updates (monotonically non-decreasing).</param>
        /// <returns>Beta value for PER IS-weights at the step.</returns>
        float GetBeta(int learnStep);
    }
}
