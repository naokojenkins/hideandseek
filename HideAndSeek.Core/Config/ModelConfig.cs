namespace HideAndSeek.Core.Config
{
    /// <summary>
    /// Model architecture and optimizer-level hyperparameters (not training loop controls).
    /// </summary>
    public class ModelConfig
    {
        // Architecture
        public int Hidden1 { get; set; } = 256;
        public int Hidden2 { get; set; } = 256;

        // Optimization
        public float LearningRate { get; set; } = 0.0005f;
        public bool UseHuberLoss { get; set; } = true;
        public float MaxGradNorm { get; set; } = 10.0f;
        public bool UseAdamW { get; set; } = true;
        public float WeightDecay { get; set; } = 0.0001f;

        // Discounting and reward processing
        public float Gamma { get; set; } = 0.99f;
        public float RewardClipAbs { get; set; } = 1.0f;
        public float RewardScale { get; set; } = 1.0f;

        // Target network
        public int UpdateTargetEvery { get; set; } = 200;
        public bool UseSoftTarget { get; set; } = true;
        public float TargetUpdateTau { get; set; } = 0.005f;

        // Epsilon-greedy
        public float EpsilonStart { get; set; } = 1.0f;
        public float EpsilonMin { get; set; } = 0.05f;
        public float EpsilonDecay { get; set; } = 0.995f;

        public string[] Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (Hidden1 <= 0) errors.Add("Model.Hidden1 must be > 0.");
            if (Hidden2 <= 0) errors.Add("Model.Hidden2 must be > 0.");
            if (LearningRate <= 0) errors.Add("Model.LearningRate must be > 0.");
            if (MaxGradNorm < 0) errors.Add("Model.MaxGradNorm must be >= 0.");
            if (TargetUpdateTau < 0 || TargetUpdateTau > 1) errors.Add("Model.TargetUpdateTau must be in [0,1].");
            if (Gamma <= 0 || Gamma > 1) errors.Add("Model.Gamma must be in (0,1].");
            if (RewardClipAbs < 0) errors.Add("Model.RewardClipAbs must be >= 0.");
            if (RewardScale <= 0) errors.Add("Model.RewardScale must be > 0.");
            if (UpdateTargetEvery < 0) errors.Add("Model.UpdateTargetEvery must be >= 0.");
            return errors.ToArray();
        }
    }
}
