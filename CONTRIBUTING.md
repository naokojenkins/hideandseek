# Contributing to Hide & Seek RL Simulator (ToolUse)

Thank you for your interest in contributing! This document describes how to propose changes and the conventions we follow.


## Code of Conduct
Please be respectful and collaborative. Assume positive intent. Disagreements are OK; keep discussions technical and constructive.


## Project layout
- ToolUse.Core: Core engine (config, RL components, 3D helpers)
- ToolUse.Sim: Simulator executable (entry point, episodes, visualization)

Target framework: .NET 9.0


## Development environment
- Install .NET 9 SDK
- Clone the repo and build: `dotnet build ToolUseCSharp.sln`
- Preferred IDEs: JetBrains Rider, Visual Studio, VS Code with C# Dev Kit


## Branching strategy (GitHub Flow)
- main is always releasable; protected branch
- Create feature branches from main: `feature/short-topic`
- Open a PR early for visibility (draft PRs welcome)
- Rebase on main as needed; keep history clean
- After review and green checks, squash-merge to main


## Commit message conventions (Conventional Commits)
Format: `<type>(optional scope): <summary>`

Types:
- feat: New feature for users or devs
- fix: Bug fix
- docs: Documentation only changes
- style: Formatting, missing semi-colons, etc.; no code change
- refactor: Code change that neither fixes a bug nor adds a feature
- perf: Performance improvements
- test: Adding or fixing tests
- build: Build system or external dependencies
- ci: CI configuration files and scripts
- chore: Other changes that don't modify src or test files

Examples:
- `feat(sim): add headless training report at the end of session`
- `fix(core): stabilize prioritized replay sampling with epsilon`


## Coding standards
- Language: C# (net9.0)
- Style:
  - Use meaningful names, PascalCase for types/methods, camelCase for locals/fields (private fields may use leading underscore `_`)
  - Prefer explicit visibility `public/private` and readonly where appropriate
  - Favor immutability and pure functions where it makes sense
  - Nullability: avoid `!` suppression; check for `null` and use guards
  - Logging: use clear prefixes like `[DEBUG]`, `[INFO]`, `[WARN]`, `[ERROR]`; avoid noisy logs in hot loops
  - Comments: English is preferred for code/docs; existing Russian comments are fine but new docs should include English
  - Exceptions: fail fast on programmer errors; handle expected runtime errors with helpful messages
- Structure:
  - Keep classes focused and small; extract helpers for reusability
  - Avoid hard-coded constants unless truly invariant; prefer configuration via GameConfig


## Tests
- If adding core logic, prefer adding unit tests (future: ToolUse.Core.Tests)
- Keep simulations deterministic when `Seed` is set; provide test hooks if practical


## Pull Request guidelines
- Keep PRs focused and small when possible; large PRs should be split into logical commits
- Include description, screenshots or logs if applicable
- Update README/CHANGELOG when changing behavior or adding features
- Ensure build succeeds: `dotnet build ToolUseCSharp.sln`
- Self-review checklist:
  - [ ] Names and APIs are clear
  - [ ] Error handling and edge cases considered
  - [ ] No dead code or commented-out blocks
  - [ ] Docs updated (README/CHANGELOG)


## Versioning strategy
- We follow Semantic Versioning (SemVer) per project (ToolUse.Core and ToolUse.Sim)
- Backward-incompatible changes to public contracts in ToolUse.Core increment the MAJOR version
- New features increment MINOR; patches increment PATCH
- See CHANGELOG.md for release notes


## Releasing
- Update CHANGELOG.md (Unreleased -> release version, date)
- Tag the commit: `v<core-version>` and/or `v<sim-version>` when releasing per-project
- Attach builds/assets as needed


## Getting help
Open a GitHub issue with a clear description, steps to reproduce, and environment details. Thank you for contributing!
