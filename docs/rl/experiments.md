# Experiments: configs and expected learning curves

This folder provides example configurations and expected learning curves to reproduce typical training runs.

How to run
- Training (headless): dotnet run --project ToolUse.Sim -- train --configPath=PATH/TO/game_config.json
- Evaluation (no learning): dotnet run --project ToolUse.Sim -- eval --configPath=PATH/TO/game_config.json --headless
- Render (3D, no learning): dotnet run --project ToolUse.Sim -- render --configPath=PATH/TO/game_config.json
- Dump effective config: dotnet run --project ToolUse.Sim -- train --configPath=... --dump-config

Example configs
- See configs/dqn_baseline.json: vanilla DQN with hard target updates.
- See configs/dqn_soft_target_per.json: DQN + PER with soft target updates.
- See configs/small_world_fast.json: small grid and short episodes for quick smoke tests.

Expected learning curves
- We include CSVs with smoothed episode reward over environment steps under curves/.
- The typical trend for baseline DQN is gradual improvement over first 50–200k steps, with variance.
- PER usually accelerates early learning and yields higher median reward at equal steps.

Reproducing the curves
1. Choose one of the configs and adjust Training.DataRoot to an output directory you can write to.
2. Start training and allow at least tens of thousands of environment steps.
3. Inspect logs in logs/ and checkpoints in models/.
4. To compare with provided curves, downsample and smooth your reward traces with a moving average window (e.g., 100 episodes).

Notes
- Seeds: Use Training.Seed (or CLI --seed) for reproducibility. The system seeds TorchSharp CPU/CUDA and internal RNGs; see Reproducibility.Initialize.
- Device: Training.Device can be Auto/Cpu/Cuda; CUDA will be used if available.
- Action space: See ActionSpaceConfig for indices of discrete actions.
