# game_config.json schema and migration

This project supports configuration via either appsettings.json or game_config.json. Both files are optional. The configuration is loaded at startup and can be overridden via environment variables and command-line options.

## Sources and precedence (lowest to highest)
1. appsettings.json (optional)
2. game_config.json (optional)
3. agents_config.json (optional; overrides Seeker/Hider sections)
4. Environment variables with prefix HNS_
5. Command-line arguments

## Environment variables
- Prefix: `HNS_`
- Use `__` (double underscore) to separate nested sections and keys.
- Examples:
  - `HNS_Seed=42`
  - `HNS_Training__BatchSize=256`
  - `HNS_Model__LearningRate=0.001`
  - `HNS_ReplayBuffer__Size=50000`

## Command-line options
Two styles are supported:
- Full keys supported by Microsoft.Extensions.Configuration.CommandLine: `--Training:BatchSize 256`
- Convenience switches:
  - `--seed=42` (mirrors both Training.Seed and root Seed)
  - `--device=cpu|cuda|auto`
  - `--batchSize=256`
  - `--stepsPerUpdate=2`
  - `--modelsPath=...`, `--logsPath=...`
  - `--configPath=path/to/game_config.json` (sets the file to load)

## Top-level structure
- Version: integer, schema version (current: 2)
- Seed: integer (legacy; Training.Seed preferred)
- World, Seeker, Hider, Actions, Session/visibility/no-progress/timeScale controls
- DQN: legacy combined section (kept for backward compatibility)
- Training: seed, batch size, steps per update, device, paths
- Model: architecture and optimizer hyperparameters
- ReplayBuffer: buffer size and PER parameters

## Agents configuration file (agents_config.json)
- Optional file placed next to game_config.json (e.g., ToolUse.Sim/configs/agents_config.json).
- If present, it overrides the Seeker and Hider sections from game_config.json.
- Schema:
```
{
  "Seeker": { /* AgentConfig fields */ },
  "Hider":  { /* AgentConfig fields */ }
}
```

See `docs/game_config.schema.json` for a formal JSON Schema.

## Backward-compatible migration
If your old `game_config.json` had only the `DQN` section:
- On load, the app migrates to v2 by splitting legacy fields:
  - Training.BatchSize <- DQN.BatchSize
  - Training.StepsPerUpdate <- DQN.StepsPerUpdate
  - Model.Hidden1/Hidden2/LearningRate/... mapped from DQN
  - ReplayBuffer.Size/WarmupSize/BetaStart/BetaEnd/BetaFrames/UseStratifiedSampling <- DQN
- The original `DQN` section is still accepted. Prefer the new sections going forward.

## Validation
At startup, the configuration is validated. If invalid, the app fails fast with actionable errors printed to stderr, for example:
- Training.BatchSize must be > 0
- World.GridSize must be > 0
- Model.TargetUpdateTau must be in [0,1]

Fix the errors and restart.

## Example (minimal)
{
  "Version": 2,
  "Seed": 12345,
  "Training": { "BatchSize": 128, "Device": "Auto" },
  "Model": { "Hidden1": 256, "Hidden2": 256, "LearningRate": 0.0005 },
  "ReplayBuffer": { "Size": 20000, "WarmupSize": 1280 }
}
