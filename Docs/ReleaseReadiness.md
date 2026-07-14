# Release Readiness

## Automated Gates

- Locked NuGet dependency restore with centralized versions.
- Recommended .NET analyzers and code-style enforcement with warnings as errors.
- Unit, integration-style contract, architecture, module asset, and JavaScript command tests.
- Dependency vulnerability audit at moderate severity or higher.
- Clean self-contained `win-x64` publish with required-content verification.
- Reproducible per-user Inno Setup installer with stable upgrade identity and data-retaining uninstall behavior.
- Published executable smoke test.
- SHA-256 checksum and reproducible ZIP release artifact.
- Optional Authenticode signing through release environment variables.

## Required Before Public Commercial Release

- Obtain the appropriate Inno Setup license before any commercial distribution.
- Obtain and protect an Authenticode certificate; sign and timestamp both the application and installer.
- Complete privacy, data retention, security threat-model, and legal/license review.
- Select and publish the intended software license and third-party notices; no license is inferred by this repository.
- Establish supported Windows/WebView2 versions and test clean install, prior-version upgrade, repair, rollback, and uninstall on that matrix.
- Add crash/diagnostic consent policy and a support process without collecting conversation content by default.
- Define semantic versioning, release notes, support lifetime, and rollback ownership.
- Run accessibility, localization-readiness, performance, long-session, and recovery testing.

The repository can produce a checksummed installer release candidate. An unsigned installer is suitable for controlled testing but is not equivalent to a fully trusted commercial distribution.
