namespace HideAndSeek.Core.Config
{
    /// <summary>
    /// Replay buffer configuration and PER-related parameters.
    /// </summary>
    public class ReplayBufferConfig
    {
        public int Size { get; set; } = 20000;
        public int WarmupSize { get; set; } = 1280; // ~10 * batch
        public bool UseStratifiedSampling { get; set; } = true;

        // PER importance sampling beta schedule
        public float BetaStart { get; set; } = 0.4f;
        public float BetaEnd { get; set; } = 1.0f;
        public int BetaFrames { get; set; } = 100000;

        public string[] Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (Size <= 0) errors.Add("ReplayBuffer.Size must be > 0.");
            if (WarmupSize < 0) errors.Add("ReplayBuffer.WarmupSize must be >= 0.");
            if (BetaStart < 0 || BetaStart > 1) errors.Add("ReplayBuffer.BetaStart must be in [0,1].");
            if (BetaEnd < 0 || BetaEnd > 1) errors.Add("ReplayBuffer.BetaEnd must be in [0,1].");
            if (BetaFrames < 0) errors.Add("ReplayBuffer.BetaFrames must be >= 0.");
            return errors.ToArray();
        }
    }
}
