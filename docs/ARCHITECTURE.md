# Architecture and Layering

This document describes the intended boundaries and layering between projects.

## Projects

- ToolUse.Core
  - Purpose: Core domain and RL abstractions/logic. Configuration, RL agents (DQN, replay buffers, schedulers), simulation domain models (state, actions), and generic abstractions.
  - Must not depend on concrete graphics libraries.
  - New: Rendering abstractions live here as interfaces (see ToolUse.Core/Rendering/IWindowRenderer.cs) so upper layers can plug in different renderers.
- ToolUse.Sim
  - Purpose: Application/entrypoint for running simulations and visualization. Composes services, chooses headless vs visualized run, persists models.
  - Contains concrete integration with graphics (Raylib) via adapter implementation of IWindowRenderer.
  - Contains an Application layer (SimulationApp) that orchestrates config, agents, world and simulation, keeping Program.cs thin.

## Boundaries

- RL/Core (ToolUse.Core/RL, ToolUse.Core/Config) contain training logic and must remain independent of any UI/graphics.
- Rendering/Visualization concerns are extracted behind the IWindowRenderer interface and implemented in ToolUse.Sim (RaylibWindowRenderer). Program and orchestrator depend only on the interface, not Raylib directly.
- Simulation and 3D utilities currently live under ToolUse.Core/RayLib and ToolUse.Core/RaylibThreeD. These are treated as simulation components and still contain direct Raylib usage for drawing. The long-term direction is to migrate drawing/input calls behind interfaces as well so core simulation can be reused without Raylib.

## Application Layer

- SimulationApp (ToolUse.Sim/Application/SimulationApp.cs) is a thin orchestrator that:
  - Loads config and seeds RNGs as needed.
  - Creates agents and the world, derives state/action sizes.
  - Runs the main simulation loop (headless or visualized) using an IWindowRenderer when visualization is enabled.
  - Saves and restores model weights/state and handles session lifecycle.
- Program.cs is intentionally kept minimal. It selects a mode and delegates to SimulationApp.

## Future Work

- Move Raylib-specific drawing and input from Simulation3D into a renderer/input abstraction, leaving Simulation3D purely simulation state and update logic.
- Consider splitting simulation types into a separate project (ToolUse.Sim.Core or ToolUse.Simulation) if we need to strictly enforce no graphics dependencies in ToolUse.Core.
- Add unit tests for orchestrator logic and small RL utilities.
