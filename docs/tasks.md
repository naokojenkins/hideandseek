# Improvement Tasks Checklist

Below is an ordered, actionable checklist to improve the repository across architecture, reliability, performance, testing, tooling, and documentation. Check items off as you complete them.

1. [ ] Repository hygiene and project metadata
   - [ ] Add a top-level README with project overview, architecture diagram, build/run instructions (CLI args, configs), and troubleshooting.
   - [ ] Add CONTRIBUTING.md with coding standards, branching strategy, commit message conventions, and PR guidelines.
   - [ ] Add a clear LICENSE file if missing.
   - [ ] Create CHANGELOG.md and define versioning strategy (SemVer) for ToolUse.Core and ToolUse.Sim.

2. [ ] Solution structure and layering
   - [ ] Document the intended boundaries between ToolUse.Core (domain + RL abstractions) and ToolUse.Sim (simulation/entrypoint/visualization).
   - [ ] Extract rendering (RayLib/RaylibThreeD) concerns behind an interface; keep RL/core logic independent of graphics.
   - [ ] Introduce an Application layer (or orchestrator) to decouple Program.cs from core training logic (thin Program that composes services).

3. [ ] Dependency management and DI
   - [ ] Introduce a minimal dependency injection composition root (e.g., Microsoft.Extensions.DependencyInjection) to wire RL components, buffers, schedulers, optimizers, and logging.
   - [ ] Replace direct new allocations with factory interfaces where appropriate (IReplayBuffer, IOptimizerFactory already present) and register them in DI.
   - [ ] Isolate TorchSharp device (CPU/GPU) selection into a single service (IDeviceProvider) and configure via settings.

4. [ ] Configuration and settings
   - [ ] Define strongly typed configuration classes (GameConfig, TrainingConfig, ModelConfig, ReplayBufferConfig) and bind from appsettings.json or game_config.json.
   - [ ] Validate configuration on startup (FluentValidation or manual guards) with actionable error messages; fail fast on invalid configs.
   - [ ] Provide environment overrides (env vars) and command-line options for key parameters (seeds, device, batch sizes, paths).
   - [ ] Introduce configuration schema/docs for game_config.json and ensure backward-compatible migrations.

5. [ ] Logging and diagnostics
   - [ ] Replace ad-hoc console prints with Microsoft.Extensions.Logging; configure log levels and sinks (console + file rolling).
   - [ ] Add structured logging for key training events: episode start/end, reward summaries, loss, buffer stats, epsilon/beta, target updates.
   - [ ] Guard critical loops with periodic metric logging to avoid silent failures.

6. [ ] Error handling and robustness
   - [ ] Replace broad catch/rethrow patterns with precise exceptions; include context.
   - [ ] Validate arrays and shapes passed into TorchSharp ops; assert on NaNs/Inf with early detection.
   - [ ] Make file IO paths robust (existence, permissions, cross-platform path separators); centralize paths in a PathService.
   - [ ] Ensure IDisposable patterns (TorchSharp tensors/optimizers/models, file streams) are correctly used to prevent leaks.

7. [ ] Reproducibility and determinism
   - [ ] Centralize random seeding; ensure System.Random, TorchSharp RNG, and any environment RNG are seeded consistently.
   - [ ] Expose a global Seed setting in configuration and log it at startup.
   - [ ] Provide a command to dump the effective configuration and seed for experiment traceability.

8. [ ] RL core: correctness and clarity
   - [ ] Add XML doc comments to IReplayBuffer, PrioritizedReplayBuffer, and related methods describing contracts (e.g., Sample invariants, index semantics).
   - [ ] In PrioritizedReplayBuffer.Sample: guard against empty buffer and insufficient size; fallback strategy or informative exception.
   - [ ] Verify and document importance-sampling weights normalization; add tests ensuring max weight == 1 and monotonicity vs. probabilities.
   - [ ] Add parameter to control stratified sampling externally; document behavior.
   - [ ] Add bounds checking in UpdatePriorities to avoid OOB; log and skip invalid indices.

9. [ ] RL scheduling and target updates
   - [ ] Expose tau for SoftTargetUpdater via config and validate range (0 < tau <= 1).
   - [ ] Add unit tests for SoftTargetUpdater to ensure convex combination invariants and no grad side effects (torch.no_grad correctness).
   - [ ] Add IBetaScheduler XML docs and tests verifying LinearBetaScheduler monotonic interpolation and clamps.

10. [ ] State modeling and serialization
    - [ ] Add XML docs for State fields, constructors, and ToArray to formalize the feature contract (order, ranges, normalization).
    - [ ] Extend FromString to provide descriptive error messages; add tests for both legacy and new formats (seen, walls).
    - [ ] Add input validation for worldSize in ToArray; add unit tests for edge cases (worldSize=0/1, KnownWallsFlat shorter/longer).
    - [ ] Consider replacing custom string format with a versioned, schema-based serialization (JSON with version field), while keeping FromString for backward compatibility.

11. [ ] Performance and memory
    - [ ] Profile memory usage of PrioritizedReplayBuffer (e.g., large arrays of float[][]); consider using contiguous float[,] or tensor-based storage.
    - [ ] Add capacity preallocation hints and avoid intermediate LINQ allocations in hot paths (Sum/Select in Sample).
    - [ ] Consider a Segment Tree or SumTree for O(log n) prioritized sampling to reduce Sample complexity.
    - [ ] Introduce batch tensorization utilities to convert float[][] to tensors efficiently (pinning or stackalloc where safe).

12. [ ] Testing strategy
    - [ ] Set up a test project (ToolUse.Tests) with xUnit/NUnit and cover core units: State, PrioritizedReplayBuffer, LinearBetaScheduler, MSELossCalculator, SoftTargetUpdater.
    - [ ] Add integration tests for a minimal training loop stub with a mock model to validate buffer-scheduler-optimizer interactions.
    - [ ] Add serialization round-trip tests for configs and State string/JSON.

13. [ ] CI/CD and quality gates
    - [ ] Add GitHub Actions (or other CI) to build on Windows/Linux/macOS with .NET 9.0.
    - [ ] Run unit tests on CI and publish artifacts (logs, coverage reports).
    - [ ] Enable code coverage (coverlet) with a minimum threshold; fail CI if below.
    - [ ] Add analyzers (Microsoft.CodeAnalysis.NetAnalyzers, StyleCop.Analyzers) and fix or baseline warnings.

14. [ ] Runtime observability and metrics
    - [ ] Integrate a metrics library (e.g., App.Metrics or custom) to track reward curves, loss, buffer size, Q-value stats.
    - [ ] Export metrics to CSV/JSON for offline analysis; optionally Prometheus format.
    - [ ] Provide a simple visualization script/notebook path to plot reward/loss over time.

15. [ ] CLI/UX improvements
    - [ ] Improve ToolUse.Sim CLI: subcommands (train, eval, render), flags for config paths, seeds, device, logging level.
    - [ ] Add a startup menu help screen and input validation with safe defaults.
    - [ ] Ensure graceful shutdown and checkpoint saving on Ctrl+C (Console.CancelKeyPress handler).

16. [ ] Checkpointing and persistence
    - [ ] Define a versioned checkpoint format for models, optimizers, and replay buffer snapshots.
    - [ ] Implement periodic autosave with retention policy; resume training from the latest checkpoint.
    - [ ] Validate paths and atomic file writes (write temp + move) to avoid corruption.

17. [ ] Cross-platform and path handling
    - [ ] Replace hardcoded path separators with Path.Combine; ensure UTF-8 filenames support.
    - [ ] Normalize data directories (logs/, models/, qtables/, configs/) under a configurable root.

18. [ ] Graphics and simulation separation
    - [ ] Abstract RayLib rendering behind an interface (IRenderer) and allow headless runs (no graphics) for CI.
    - [ ] Move input handling and HUD printing out of Program.cs into dedicated services to reduce class size and complexity.

19. [ ] Threading and async
    - [ ] Audit for potential race conditions if training/inference/rendering are multi-threaded; add synchronization or channels.
    - [ ] Use cancellation tokens for long-running loops; ensure cooperative cancellation on shutdown.

20. [ ] Documentation of RL algorithms and experiments
    - [ ] Provide a docs/rl/ folder describing algorithm choices (DQN, PER, target updates), hyperparameters, and references.
    - [ ] Add example experiment configs and expected learning curves.

21. [ ] Safety checks and guardrails
    - [ ] Add invariant checks for action indices vs ActionSpaceConfig.Count; ensure callers stay within bounds.
    - [ ] Validate ActionSpaceConfig consistency (no duplicate indices, Count >= max + 1); add unit tests.
    - [ ] Add argument validation to public APIs (throw ArgumentOutOfRangeException/ArgumentNullException appropriately).

22. [ ] Packaging and distribution
    - [ ] Add dotnet tool or publish profiles for ToolUse.Sim; document self-contained publish for major OS targets.
    - [ ] Create NuGet package for ToolUse.Core if intended to be reused; include XML docs and symbols.

23. [ ] Housekeeping and cleanup
    - [ ] Remove generated artifacts (obj/, bin/, qtables/, models/) from VCS or move to .gitignore if not already ignored.
    - [ ] Ensure that large files (qtables, models) are excluded or handled via LFS if necessary.

24. [ ] Code style and consistency
    - [ ] Normalize naming conventions (PascalCase for public types/members, fields naming, readonly where applicable).
    - [ ] Replace magic numbers with named constants or config; e.g., epsilon in PER, default tau, default beta schedule.
    - [ ] Reduce LINQ in hot paths; prefer loops where performance is critical.

25. [ ] Future enhancements (nice-to-have)
    - [ ] Implement double Q-learning/dueling networks options if applicable to the domain.
    - [ ] Add curriculum or reward shaping toggles and documentation.
    - [ ] Provide a plug-in interface for alternative replay buffers and loss calculators.
