Project: HideAndSeek (Core, Sim, Tests) — Advanced Development Guidelines

This document captures project-specific knowledge to streamline development, debugging, and testing.

1. Build and Configuration
- Toolchain
  - .NET SDK: net9.0 (confirmed by project targets and CI/tests).
  - IDEs: JetBrains Rider recommended; VS Code + C# Dev Kit also works.
- Solution layout
  - HideAndSeek.Core — core engine: config, RL, rendering adapters, math, utilities.
  - HideAndSeek.Sim — simulation app (entry point) driven by configs under HideAndSeek.Sim/configs.
  - HideAndSeek.Tests — xUnit tests covering config roundtrips, RL guardrails, loss calculators, schedulers, replay buffer, and simple integration.
- Build
  - From repo root: dotnet build -c Debug
  - Typical restore/build is fast; no native prerequisites are required to build. Rendering code paths compile on all platforms; runtime may require GPU/graphics (see below).
- Runtime configuration (Sim)
  - Primary config: HideAndSeek.Sim/configs/game_config.json
    - Training.* controls RL training loop (batch size, updates, device selection, autosave).
    - Model.* configures DQN hyperparameters (hubers loss toggle, AdamW, target network updates, epsilon schedule).
    - ReplayBuffer.* sets capacity and prioritization (Beta schedule used for PER when UseStratifiedSampling=true).
    - World.* defines grid and geometry.
    - Actions maps action names to discrete indices; Count must match total distinct actions.
  - Device selection
    - Training.Device supports Auto/CPU/GPU; when Auto, the runtime attempts GPU if available and falls back to CPU.
  - Filesystem
    - Training.DataRoot is the anchor for ModelsPath and LogsPath; ensure paths exist or are creatable by the process.

2. Testing: Running and Adding Tests
- Framework: xUnit
  - Attributes: [Fact] for single-case tests; [Theory] + [InlineData] or data sources for parametrized tests.
- Running all tests
  - From repo root: dotnet test -c Debug
  - Using the JetBrains toolchain in this environment (validated): the orchestration equals running all tests in solution and they pass (42/42 at time of writing).
- Running specific tests
  - Namespace/Class filter (Rider/IDE preferable), or CLI example:
    - dotnet test --filter FullyQualifiedName~HideAndSeek.Tests.ConfigSerializationTests
    - dotnet test --filter "FullyQualifiedName=HideAndSeek.Tests.SoftTargetUpdaterTests.Update_AlphaBetween0And1_Interpolates"
- Adding a new test
  - Place in HideAndSeek.Tests with suffix *Tests.cs within namespace HideAndSeek.Tests.
  - Reference production internals through appropriate public APIs; avoid heavy allocations inside tight assertions to keep suite fast.
  - If you need integration with configs, prefer constructing config objects directly rather than reading/writing files where possible; there are existing serialization tests for roundtrips if you need file IO.
- Example minimal test (validated)
  - We verified a smoke test compiles and runs using xUnit. Example snippet:
    using Xunit;
    
    namespace HideAndSeek.Tests
    {
        public class SmokeDemoTests
        {
            [Fact]
            public void Smoke_Passes()
            {
                Assert.True(1 + 1 == 2);
            }
        }
    }
  - Run only this test (two equivalent ways):
    - dotnet test --filter FullyQualifiedName~HideAndSeek.Tests.SmokeDemoTests
    - dotnet test --filter "FullyQualifiedName=HideAndSeek.Tests.SmokeDemoTests.Smoke_Passes"

3. Test Authoring Guidance (Project-Specific)
- Config validation
  - See ActionSpaceConfigTests for examples that validate action indices and count consistency. When changing action enums or mappings in configs, add/update tests to detect mismatches early.
- Serialization roundtrips
  - ConfigSerializationTests demonstrate JSON roundtrips for Training/ReplayBuffer/Model configs. If you change config schema or defaults, update these tests and ensure backward-compatible deserialization when feasible.
- RL algorithm guardrails
  - DQNAgentGuardrailsTests expect explicit argument checks (state length, sizes). When refactoring model inputs/outputs, keep these guards intact and extend tests for new preconditions.
- Numerical stability
  - MSELossCalculatorTests and SoftTargetUpdaterTests contain invariants (e.g., interpolation with tau in [0,1]). Maintain these invariants; if you introduce new loss functions or target update strategies, add tests that lock down edge behavior (NaN/Inf handling, clipping, gradient caps).
- Replay buffer
  - ReplayBufferTests include PER-related expectations (Beta schedule, stratified sampling flag). If you alter sampling or prioritization, update both behavior and tests in tandem.
- Integration and runtime limits
  - IntegrationTrainingLoopTests are relatively light; keep them fast. Avoid making integration tests depend on GPU or real-time rendering. If you add heavier flows, provide a CPU-safe, deterministic path for tests.

4. Running the Simulation
- CLI
  - From repo root: dotnet run -c Debug --project HideAndSeek.Sim
  - To select a config: ensure HideAndSeek.Sim reads HideAndSeek.Sim/configs/game_config.json by default. If you add CLI args for alternate configs, document them in README and mirror here.
- Determinism and speed
  - Seed in root config controls deterministic runs where feasible. TimeScale accelerates sim; use lower TimeScale for debugging to reduce non-deterministic timing effects.
- Logging & checkpoints
  - Training.LogsPath and ModelsPath within DataRoot control output. AutosaveSeconds and CheckpointKeepLast bound checkpoint churn.

5. Code Style & Conventions
- General
  - C# 12/.NET 9 features allowed. Prefer explicit access modifiers and readonly where applicable.
  - Use immutable config record types or init-only setters where practical to prevent mutation during training.
- Nullability and guard clauses
  - Enable nullable reference types in new files; keep public APIs defensive (see Guardrails tests).
- Naming
  - Tests: <UnitUnderTest>Tests.<Behavior>_<Condition>_<Expectation> for clarity and easy filtering.
- Performance
  - Hot paths in Core/RL should avoid LINQ in tight loops; prefer spans/arrays and pre-allocated buffers.
  - Clamp/validate hyperparameters once at construction time; avoid per-step validation overhead.

6. Practical Troubleshooting
- Build errors
  - Ensure .NET 9 SDK is installed (dotnet --info). Clean/rebuild if moving between SDK versions: dotnet clean; dotnet restore; dotnet build.
- Test flakiness
  - Check seed usage and TimeScale in configs when using integration-style tests. Keep tests independent of wall-clock time.
- GPU/Rendering
  - Rendering abstractions compile headless; avoid invoking GPU-only code in tests. The Sim can run on CPU if Training.Device=CPU or Auto falls back.

7. Verified Commands (as of 2025-09-02)
- Build: dotnet build -c Debug (succeeds)
- Run all tests: dotnet test -c Debug (42/42 passing at time of writing)
- Run specific test: dotnet test --filter "FullyQualifiedName=HideAndSeek.Tests.SoftTargetUpdaterTests.Update_AlphaBetween0And1_Interpolates"

Notes
- When altering config schema, update both docs and tests to reflect new defaults and validation rules.
- Keep integration tests light to preserve fast inner loop times.
