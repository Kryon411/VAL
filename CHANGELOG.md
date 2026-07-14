# Changelog

Notable changes are recorded here. VAL follows semantic versioning once a public release line is established.

## Unreleased

### Changed

- Separated desktop packaging, WPF composition, host services, feature workflows, contracts, and storage into enforced project boundaries.
- Extracted Pulse and Chronicle state machines from the Continuum command adapter.
- Added supervised background work and bounded asynchronous application shutdown.
- Added a per-session single-instance desktop guard.
- Centralized and locked NuGet dependencies with vulnerability auditing.
- Added architecture, module asset, JavaScript command, startup recovery, and workflow lifecycle tests.
- Added reproducible publish output, checksums, ZIP packaging, optional Authenticode signing, and published smoke testing.

### Fixed

- Fresh installs no longer enter safe mode because the startup-state file does not yet exist.
- Chronicle completion failures no longer leave the operation coordinator permanently occupied.
- Abyss background failures are observed and cancellation-aware.
