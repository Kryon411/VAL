# VAL v5.0.0 - Architecture, Reliability, and Release Engineering

VAL v5.0.0 establishes the new commercial-style desktop architecture as the baseline for future feature work. The familiar desktop experience is retained while ownership boundaries, runtime lifecycle behavior, persistence, testing, and packaging are substantially strengthened.

## Highlights

- Split the desktop executable, WPF application, host infrastructure, contracts, and the Abyss, Continuum, and Truth domains into explicit assemblies.
- Replaced legacy static runtime bridges with dependency-injected services and clear composition ownership.
- Decomposed the desktop shell, startup flow, toast system, command registration, and Continuum workflows into focused components.
- Added supervised background work, bounded shutdown, single-instance enforcement, startup recovery, and atomic UI state persistence.
- Expanded architecture, workflow, JavaScript contract, storage, security, and lifecycle test coverage.
- Added locked dependencies, vulnerability auditing, deterministic source manifests, reproducible publishing, checksums, and published smoke testing.
- Added a reproducible Inno Setup installer that preserves existing VAL user data across upgrades and uninstall.
- Separated installed program files from mutable per-user data for new v5 installations.

## Installation

1. Download `VAL_Setup_v5.0.0.exe` below.
2. Run the installer for the current Windows user; administrator elevation is not required.
3. Existing VAL data under `%LocalAppData%\VAL` is retained during upgrade and uninstall.

The installer checksum is provided as `VAL_Setup_v5.0.0.exe.sha256`. This build is not Authenticode-signed unless the release asset reports a valid digital signature, so Windows SmartScreen may display an unrecognized-app warning.

## Upgrade Notes

- The installer retains VAL's existing application identity and upgrades prior installer-based versions.
- v5 program files are installed under `%LocalAppData%\Programs\VAL`.
- Conversation memory, state, logs, profile data, and user settings remain under `%LocalAppData%\VAL`.
- Known legacy program payload is removed only after the v5 payload is installed successfully; user data is never part of installer cleanup.

## Reporting Issues

Please open a GitHub issue with the VAL version, Windows version, steps to reproduce, and screenshots or diagnostics where appropriate. Do not attach private conversation content.
