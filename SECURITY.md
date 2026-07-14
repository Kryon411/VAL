# Security Policy

## Supported Versions

VAL is pre-release software. Security fixes are applied to the latest development revision and the latest published release candidate only.

## Reporting A Vulnerability

Use the repository's private GitHub security advisory reporting channel. Do not include conversation archives, authentication data, screenshots, or other personal content unless explicitly requested through a secure channel.

Please include the affected version, reproduction steps, impact, and whether the issue requires local access. Avoid filing a public issue until a fix or mitigation is available.

## Security Boundaries

- WebView messages are accepted only from allowed origins and validated against registered command names.
- Feature code uses host abstractions for paths, processes, UI dispatch, and background work.
- Local archive writes use safe-path and atomic-file primitives.
- Release builds audit NuGet dependencies and support Authenticode signing.

See [Docs/ThreatModel.md](Docs/ThreatModel.md) for the detailed trust model and residual risks.
