Expected learning curves

These are illustrative, smoothed curves (moving average over 100 episodes) to indicate expected qualitative behavior. Actual results may vary by seed and hardware.

- dqn_baseline_expected.csv: Baseline DQN with hard target updates.
- dqn_per_expected.csv: DQN + PER with soft target updates (Tau=0.005).

Columns
- step: environment steps (x-axis)
- avg_reward: moving-average episode reward (y-axis)
