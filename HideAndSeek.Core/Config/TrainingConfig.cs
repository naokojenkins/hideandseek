using System;

namespace HideAndSeek.Core.Config
{
    /// <summary>
    /// Training-related configuration (seeds, batch sizes, frequency, paths, device).
    /// Kept separate from model architecture and environment/game rules.
    /// </summary>
    public class TrainingConfig
    {
        /// <summary> Global seed for reproducibility. Overrides GameConfig.Seed if specified. </summary>
        public int? Seed { get; set; }

        /// <summary> Steps per environment step to perform optimization. </summary>
        public int StepsPerUpdate { get; set; } = 2;

        /// <summary> Batch size for training. </summary>
        public int BatchSize { get; set; } = 128;

        /// <summary> Device preference (Auto/Cpu/Cuda/Mps) if supported by runtime. </summary>
        public string Device { get; set; } = "Auto";

        /// <summary>
        /// Root directory for data (logs/, models/, qtables/, configs/). Can be absolute or relative to application base directory.
        /// </summary>
        public string DataRoot { get; set; } = ".";

        /// <summary> Path to store models/checkpoints. If relative, it is resolved under DataRoot. </summary>
        public string ModelsPath { get; set; } = "models";

        /// <summary> Path to logs. If relative, it is resolved under DataRoot. </summary>
        public string LogsPath { get; set; } = "logs";

        /// <summary> Periodic autosave interval in seconds. 0 disables autosave timer (saves still happen on episode end/shutdown). </summary>
        public int AutosaveSeconds { get; set; } = 300;

        /// <summary> How many latest checkpoints to keep. Must be >= 1. </summary>
        public int CheckpointKeepLast { get; set; } = 5;

        /// <summary> If true, resume from the latest available checkpoint on startup. </summary>
        public bool ResumeFromLatest { get; set; } = true;

        /// <summary> Validate and return error messages if any. </summary>
        public string[] Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (BatchSize <= 0) errors.Add("Training.BatchSize must be > 0.");
            if (StepsPerUpdate < 0) errors.Add("Training.StepsPerUpdate must be >= 0.");
            if (string.IsNullOrWhiteSpace(DataRoot)) errors.Add("Training.DataRoot is required.");
            if (string.IsNullOrWhiteSpace(ModelsPath)) errors.Add("Training.ModelsPath is required.");
            if (string.IsNullOrWhiteSpace(LogsPath)) errors.Add("Training.LogsPath is required.");
            if (AutosaveSeconds < 0) errors.Add("Training.AutosaveSeconds must be >= 0.");
            if (CheckpointKeepLast < 1) errors.Add("Training.CheckpointKeepLast must be >= 1.");
            return errors.ToArray();
        }
    }
}
