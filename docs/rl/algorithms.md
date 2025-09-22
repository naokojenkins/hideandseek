# RL Algorithms used in hideandseek

This project trains DQN-based agents for a Hide & Seek environment. It combines:

- Deep Q-Networks (DQN) for value-based control over a discrete action space.
- Prioritized Experience Replay (PER) to improve sample efficiency by replaying TD-error-salient transitions more often.
- Target networks with both hard and soft (Polyak/EMA) update strategies to stabilize temporal-difference targets.

The simulator supports training (headless) and evaluation or rendering via ToolUse.Sim.

DQN
- We learn an action-value function Q(s, a; θ) with a small MLP (see ModelConfig) and optimize a TD loss using Huber or MSE.
- Target values use a separate target network with parameters θ−.
- Action selection uses epsilon-greedy scheduling with parameters EpsilonStart, EpsilonMin, EpsilonDecay.

Temporal-Difference target
Given a transition (s, a, r, s', done):
- y = r + γ * (1 − done) * max_{a'} Q_target(s', a').
- Loss is Huber(y − Q_online(s, a)) when UseHuberLoss=true, otherwise MSE.
- Gradients can be clipped by MaxGradNorm.

Target network updates
- Hard: copy θ to θ− every UpdateTargetEvery steps.
- Soft (Polyak): θ− ← (1 − τ) θ− + τ θ at every optimization step (τ = TargetUpdateTau). See ToolUse.Core.RL.SoftTargetUpdater.

Prioritized Experience Replay (PER)
- Samples are drawn with probability proportional to priority p_i (typically |δ_i| + ε).
- Importance-sampling weights w_i correct bias: w_i = (N * P(i))^{−β} / max_j w_j, with β annealed from BetaStart to BetaEnd over BetaFrames.
- Optionally UseStratifiedSampling to reduce variance.

Exploration and environment specifics
- Epsilon-greedy scheduling helps exploration in the discrete action space defined by ActionSpaceConfig (e.g., TurnLeft, Forward, Idle, Backward, etc.).
- Rewards are shaped for the two roles (Seeker/Hider). See GameConfig.AgentConfig fields. Some shaping (e.g., RewardWhenSeenBySeeker) can be applied on the agent side when ApplyVisibilityShapingInAgent=true.

Device and performance
- TorchSharp is used for tensor operations. Training can prefer CPU/CUDA via Training.Device with automatic fallbacks.

References
- See references.md for canonical papers on DQN, PER, target networks and related techniques.
