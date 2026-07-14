# Release Readiness

## Automated Gates

- Locked NuGet dependency restore with centralized versions.
- Recommended .NET analyzers and code-style enforcement with warnings as errors.
- Unit, integration-style contract, architecture, module asset, and JavaScript command tests.
- Dependency vulnerability audit at moderate severity or higher.
- Clean self-contained `win-x64` publish with required-content verification.
- Published executable smoke test.
- SHA-256 checksum and reproducible ZIP release artifact.
- Optional Authenticode signing through release environment variables.

## Required Before Public Commercial Release

- Obtain and protect an Authenticode certificate; sign both the application and future installer.
- Select and build a signed installer and a safe update/rollback strategy.
- Complete privacy, data retention, security threat-model, and legal/license review.
- Select and publish the intended software license and third-party notices; no license is inferred by this repository.
- Establish supported Windows/WebView2 versions and test clean install, upgrade, repair, and uninstall on that matrix.
- Add crash/diagnostic consent policy and a support process without collecting conversation content by default.
- Define semantic versioning, release notes, support lifetime, and rollback ownership.
- Run accessibility, localization-readiness, performance, long-session, and recovery testing.

The repository can produce a release candidate, but unsigned ZIP distribution is not equivalent to a finished commercial installer.
