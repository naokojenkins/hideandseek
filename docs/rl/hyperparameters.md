# Hyperparameters and configuration mapping

The simulator uses a hierarchical JSON configuration (game_config.json), deserialized into GameConfig. The schema is versioned (Version = 2) and split into sections: Training, Model, ReplayBuffer, plus environment and agents.

Key sections and fields

TrainingConfig (ToolUse.Core.Config.TrainingConfig)
- Seed: Optional override for the global seed (falls back to GameConfig.Seed).
- StepsPerUpdate: How many optimization steps per environment step.
- BatchSize: Training batch size.
- Device: "Auto" | "Cpu" | "Cuda" (if available).
- DataRoot: Root folder for models, logs, configs.
- ModelsPath: Folder under DataRoot for checkpoints.
- LogsPath: Folder under DataRoot for logs.
- AutosaveSeconds: Periodic autosave interval (0 disables timer).
- CheckpointKeepLast: How many latest checkpoints to keep.
- ResumeFromLatest: Resume from latest checkpoint on startup.

ModelConfig (ToolUse.Core.Config.ModelConfig)
- Hidden1, Hidden2: MLP widths for Q-network.
- LearningRate: Optimizer LR (Adam/AdamW).
- UseHuberLoss: Use smooth L1 instead of MSE.
- MaxGradNorm: Gradient norm clipping (0 disables).
- UseAdamW, WeightDecay: AdamW options.
- Gamma: Discount factor.
- RewardClipAbs: Absolute clip of reward before scaling (0 disables).
- RewardScale: Multiply reward after clipping.
- UpdateTargetEvery: Hard target update frequency (steps).
- UseSoftTarget: If true, use Polyak updates instead of hard copies.
- TargetUpdateTau: Polyak coefficient τ in (0, 1].
- EpsilonStart, EpsilonMin, EpsilonDecay: Epsilon-greedy schedule.

ReplayBufferConfig (ToolUse.Core.Config.ReplayBufferConfig)
- Size: Replay capacity.
- WarmupSize: Minimum fill before training starts.
- UseStratifiedSampling: PER sampler variance reduction.
- BetaStart, BetaEnd, BetaFrames: Importance sampling annealing schedule.

Game/environment
- World: GridSize, CellSize, WallHeight, RoomSize.
- SessionDurationSeconds: Episode length in seconds.
- FramesForCatch: Consecutive visible frames to count a catch.
- Seed: Global seed (used if Training.Seed is not set).
- ActionRepeat, VisibilityCheckInterval, NoProgress* parameters, MinInitialSeparation, TimeScale.
- Actions (ActionSpaceConfig): Discrete action indices and Count.
- Seeker, Hider (AgentConfig): Reward shaping and physical parameters (VisionRadius, Speed, etc.). Also includes ForceExploitWhenSeen and ApplyVisibilityShapingInAgent.

Validation
- GameConfig.Validate() checks ranges for most fields. If you see startup validation errors, adjust your config accordingly.

Notes on duplication with legacy DQNConfig
- DQNConfig remains for backward compatibility; when Version < 2 the loader migrates fields into Training/Model/ReplayBuffer. Prefer using the newer sections directly.

Recommended starting values
- LearningRate: 5e-4
- Gamma: 0.99
- BatchSize: 128
- Replay Size: 20,000–100,000
- WarmupSize: ~10 × BatchSize
- Epsilon: Start=1.0, Min=0.05, Decay=0.995 (or linear steps)
- Target updates: UseSoftTarget=true with Tau=0.005 or hard UpdateTargetEvery=200
- PER beta: 0.4 → 1.0 over 100k updates

Tuning tips
- If learning oscillates, try lowering LR, enabling Huber loss, and increasing UpdateTargetEvery or decreasing Tau.
- If training is slow to improve, consider larger replay buffer, slightly larger batch, or more frequent updates (StepsPerUpdate).
- If over-exploration persists, increase EpsilonDecay rate or reduce EpsilonMin.
