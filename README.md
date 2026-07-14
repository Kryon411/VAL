# VAL (Virtual Assistant Layer)

VAL is a Windows desktop shell for ChatGPT with explicit, local, user-invoked tools for continuity, recall, capture, and interface control.

## Principles

- **Explicit:** features run in response to user actions.
- **Deterministic:** important outputs are file-backed and traceable.
- **Local-first:** archives, diagnostics, and preferences remain on the device.
- **Modular:** desktop, host, storage, and feature concerns have enforced project boundaries.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK for development (see `global.json`)
- Microsoft Edge WebView2 Evergreen Runtime

## Build And Test

```powershell
dotnet restore VAL.sln --locked-mode
dotnet build VAL.sln -c Release --no-restore
dotnet test MAIN/VAL.Tests/VAL.Tests.csproj -c Release --no-build --no-restore
```

Run the development build:

```powershell
dotnet run --project MAIN/VAL.Desktop/VAL.Desktop.csproj
```

The executable is written under `MAIN/VAL.Desktop/bin/<Configuration>/net8.0-windows/win-x64/`.

## Publish

```powershell
./Build/Publish_Release.ps1
```

The release script performs a locked restore, creates a clean self-contained publish, optionally signs `VAL.exe`, writes a SHA-256 checksum, and creates versioned application and private support-symbol archives under `PRODUCT`.

Build the per-user Windows installer with Inno Setup 6:

```powershell
./Build/Build_Installer.ps1
```

The installer builder validates the product version, publishes VAL, compiles `Installer/VAL.iss`, optionally signs the installer, and writes a SHA-256 checksum under `InstallerOutput`.

For signed releases, set `VAL_SIGNING_CERTIFICATE_PATH` and `VAL_SIGNING_CERTIFICATE_PASSWORD` before publishing.

## Modules

- **Continuum:** maintains local conversation archives and creates Pulse handoffs.
- **Abyss:** searches archived sessions and returns results with source provenance.
- **Portal:** stages user-requested captures for sending.
- **Void:** reduces expensive or distracting page content.
- **VALTheme:** applies VAL's desktop visual treatment.

Application data and logs default to `%LOCALAPPDATA%\VAL`. See `MAIN/appsettings.json` for configuration.

## Documentation

- [Architecture](Docs/Architecture.md)
- [Release readiness](Docs/ReleaseReadiness.md)
- [Threat model](Docs/ThreatModel.md)
- [Privacy and local data](Docs/Privacy.md)
- [Modules](Docs/Modules.md)
- [Build smoke checklist](Docs/BuildSmokeChecklist.md)

## Project Status

VAL can produce a tested, checksummed installer release candidate with commercial-style architecture and automated quality gates. A fully trusted commercial distribution still requires Authenticode signing, an update/rollback channel, legal and privacy review, the appropriate installer license, and a supported-machine test matrix.
