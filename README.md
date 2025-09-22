# Hide & Seek RL Simulator (ToolUse)

A .NET 9 solution for training and simulating a two-agent Hide-and-Seek environment. It uses:
- Raylib for 3D visualization (via Raylib-cs bindings)
- TorchSharp (PyTorch for .NET) for Deep Q-Network (DQN) agents
- A JSON-driven configuration system that can run fully headless (console) or with real-time 3D rendering

Projects in the solution:
- ToolUse.Core — Core engine: config, RL components, and 3D world helpers
- ToolUse.Sim — Simulator executable: orchestrates training/episodes, visualization, and persistence

Status: early development (pre-1.0); public API/behavior may change.


## Architecture overview

```
sequenceDiagram
    participant Sim as ToolUse.Sim (Program)
    participant Core as ToolUse.Core
    participant RL as RL (DQN)
    participant Viz as Raylib 3D
    participant Cfg as GameConfig (JSON)

    Sim->>Cfg: Load GameConfig.Instance (game_config.json or defaults)
    Sim->>Core: Build World3D, Agents (Seeker/Hider)
    Sim->>RL: Initialize DQNAgent for Seeker/Hider
    loop Episode Steps
      Sim->>Core: Step world (action repeat, visibility checks)
      RL->>Core: Choose actions, compute rewards
      Sim->>Viz: Optional draw frame (3D)
    end
    Sim->>Sim: Save models/states (models/*.pt, *_state.json)
```

Key namespaces/components:
- ToolUse.Core.Config: GameConfig (central JSON config), ActionSpaceConfig, DQNConfig, etc.
- ToolUse.Core.RL: DQNAgent and training utilities (replay buffer, loss, schedulers)
- ToolUse.Core.RaylibThreeD: World3D, Agent3D, and rendering helpers
- ToolUse.Sim: Program entry point, episode loop, menu, persistence


## Requirements
- .NET 9 SDK (https://dotnet.microsoft.com/)
- OS support for Raylib native libraries (bundled under ToolUse.Sim/bin/.../runtimes)
- TorchSharp (CPU works out-of-the-box; CUDA requires a compatible NVIDIA driver + CUDA runtime)


## Build
- Restore and build the solution:
  - dotnet restore
  - dotnet build ToolUseCSharp.sln -c Release


## Run
- From the repository root:
  - dotnet run --project ToolUse.Sim -c Release
- At startup you will be prompted to choose a mode:
  - 1 — Console (no visualization)
  - 2 — 3D visualization (Raylib window)

Notes:
- Currently there are no CLI arguments; the mode is chosen interactively.
- Models and agent state are stored under models/ relative to the working directory of the simulator (e.g., ToolUse.Sim/bin/Debug/net9.0/models/).


## Configuration
- The simulator reads a JSON file named game_config.json in the current working directory.
- If the file is missing, sensible defaults are used (see ToolUse.Core/Config/GameConfig.cs for all fields and defaults).
- You can copy and edit the generated game_config.json from a previous run under ToolUse.Sim/bin/... to the folder where you run the app.

Selected fields (not exhaustive):
- World: GridSize, CellSize, WallHeight, RoomSize
- Agents: rewards, speed, vision, rotation, penalties
- DQN: network sizes, gamma, epsilon schedule, batch size, replay buffer, optimizer, target updates
- Session: SessionDurationSeconds, FramesForCatch, Seed, ActionRepeat, VisibilityCheckInterval, NoProgressSeconds
- Actions: semantic action list and count used by the agents

Tip: For reproducibility set Seed in the config (both Raylib and TorchSharp seeding are attempted).


## Logs, models, and outputs
- models/: seeker.pt, hider.pt, seeker_state.json, hider_state.json
- logs/: runtime logs (if enabled in your environment)
- qtables/: simple tabular trackers if used by your setup


## Troubleshooting
- Native library load errors (Raylib):
  - Ensure you run from a folder where the runtimes/ subfolder is available (dotnet run uses the project output with runtimes included).
  - On Linux/macOS, you may need to allow loading native libraries from the app output.
- Headless servers:
  - Choose console mode (1) or disable X/Window requirements.
- TorchSharp CUDA not used:
  - The build works on CPU by default. Ensure a compatible CUDA runtime for GPU; otherwise it will gracefully fall back to CPU.
- Config not applied:
  - Verify that game_config.json is present in the working directory from which you run the simulator.
- High DPI or window issues:
  - Use console mode for stability or adjust OS display scaling settings.


## Versioning
- This repository follows Semantic Versioning (SemVer) per project (ToolUse.Core and ToolUse.Sim).
- See CHANGELOG.md for releases and "Unreleased" changes.


## Contributing
See CONTRIBUTING.md for coding standards, branching strategy, commit conventions, and PR guidelines.


## License
This project is licensed under the MIT License — see LICENSE for details.
