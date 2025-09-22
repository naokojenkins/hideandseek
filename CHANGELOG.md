# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning (SemVer). Versioning is applied per project: ToolUse.Core and ToolUse.Sim. When shared contracts in ToolUse.Core change incompatibly, increment the MAJOR version for Core and adjust Sim as needed.

## [Unreleased]
### Added
- Repository hygiene and project metadata: README.md, CONTRIBUTING.md, LICENSE, CHANGELOG.md

### Changed
- Documented build/run instructions, configuration behavior, and troubleshooting guidance in README.


## [0.1.0] - 2025-08-25
### Added
- Initial public structure for ToolUse.Core (Config, RL components, Raylib helpers)
- Initial ToolUse.Sim with interactive mode selection (console or 3D visualization)
- JSON-based configuration with sensible defaults (GameConfig)
- Model persistence under models/ (seeker.pt, hider.pt, *_state.json)

### Notes
- Early development; APIs may evolve before 1.0


[Unreleased]: https://example.com/compare/v0.1.0...HEAD
[0.1.0]: https://example.com/releases/tag/v0.1.0
