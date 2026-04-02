# Changelog

All notable changes to this project are documented in [GitHub Releases](https://github.com/Bia10/Maple.StringPool/releases).

This project uses [MinVer](https://github.com/adamralph/minver) for semantic versioning based on git tags.

## [0.1.0] - 2026-04-02

### Added

- Comprehensive unit tests for `RotatedKey`: constructor validation, indexer wrapping, and full decode integration.
- Unit tests for `NativeTypes` (`EncodedEntryLayout`, `TypeSizes`, pointer-size constants).
- Unit tests for `MemoryPeImageReader`: factory methods (`FromFile`, `FromBytes`, `FromMemory`) and disposal.
- Unit tests for `StringPoolDecoder` factory methods (`Open`, `FromBytes`, `FromMemory`) and null-reader constructor guard.
- Additional `RotateLeftInPlace` edge-case tests: single-element key carry-wraps-to-zero and shift-8 no-op.

### Changed

- Renamed `Maple.StringPool.XyzTest` → `Maple.StringPool.DocTest` to match project conventions.
- Moved the `Public API Reference` section from `README.md` into `docs/PublicApi.md` so the README page loads faster.
- Enabled `IsTrimmable`, `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAOTAnalyzer` on `Maple.StringPool.Cli` for NativeAOT/trimming enforcement.

### CI

- Extracted coverage collection into a dedicated `coverage` job with Codecov upload.
- Upgraded `step-security/harden-runner` to v2.16.0 and pinned all action SHAs.
