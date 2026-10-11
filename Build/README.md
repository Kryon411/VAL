# Release Publishing

From the repository root, using PowerShell 7 for the validation scripts:

Install the exact .NET 10 SDK pinned in `global.json`. A verified, repository-local SDK at `artifacts/tools/dotnet` is also recognized by the build helpers. The SDK cache is ignored and must not be packaged.

```powershell
./Build/Publish_Release.ps1
```

The script publishes `MAIN/VAL.Desktop/VAL.Desktop.csproj` to `PRODUCT/Publish`, verifies required runtime content, writes `VAL.exe.sha256`, and creates versioned application and private support-symbol archives under `PRODUCT`.

Use `-FrameworkDependent` only for internal diagnostics. Set `VAL_SIGNING_CERTIFICATE_PATH` and `VAL_SIGNING_CERTIFICATE_PASSWORD` to sign release candidates.

Run the published smoke test with:

```powershell
./PRODUCT/Publish/VAL.exe --smoke --smoke-timeout-ms=30000
```

Smoke mode uses a unique temporary data directory, browser profile, configuration override, and crash-guard sentinel. It does not exercise the user's existing archives or authenticated profile. Its report records the scratch paths; remove that scratch directory after the smoke process and browser subprocesses have exited if retention is not needed.

Run client validation with `node Build/Verify_ClientScripts.js` and the fail-on-findings dependency gate with `./Build/Verify_Dependencies.ps1`. The dependency gate checks every solution project and fails when an advisory is reported, even though the underlying `dotnet list package` command can return success.

For an unreleased review build, use a validation root under `artifacts`. The helpers refuse output roots outside that tree and paths traversing directory links:

```powershell
./Build/Publish_Release.ps1 -ValidationOutputRoot artifacts/review/release-validation
./Build/Verify_PublishedRelease.ps1 -ValidationOutputRoot artifacts/review/release-validation
```

Published validation launches isolated GUID-named temporary profiles, checks checksums and symbol separation, exercises locked cleanup/retry in new processes, and checks that invalid cleanup requests block startup. Successful startup also requires `NativeThemeReady: True`: the real main window must resolve its theme, and its owned transparent recovery window must contain only the styled Return pill, with resolved typography and rounded button template. Loading the resource dictionary alone is not sufficient. Reports are written under the validation root. `--smoke-data-root` can reuse only a GUID-named directory directly under `Temp/VAL.Smoke`; normal user data paths are rejected. The CI runner skips interactive WebView checks as before, but still checks the native theme, while local validation exercises the installed runtime. Do not upload review artifacts using an existing release version.

Build the versioned per-user installer with Inno Setup 6:

```powershell
./Build/Build_Installer.ps1
```

The installer script is tracked at `Installer/VAL.iss`. The builder verifies that its version matches `Directory.Build.props`, publishes VAL, validates `VAL.exe` version metadata, compiles `InstallerOutput/VAL_Setup_v<version>.exe`, and writes a SHA-256 checksum. Set `INNO_SETUP_COMPILER` when `ISCC.exe` is not installed in a standard location.

`installer-build.json` binds the installer to both the executable and the complete published payload. The payload digest includes sorted relative filenames and each file's SHA-256, so asset-only changes and renames invalidate earlier validation. Compilation and published checks refuse payload changes during their run; installer verification and machine-kit staging reject mismatched build metadata. After any publish change, rebuild the installers and rerun their gates.

Set `VAL_SIGNING_CERTIFICATE_PATH` and `VAL_SIGNING_CERTIFICATE_PASSWORD` to timestamp-sign both `VAL.exe` and the installer. Without these values, the build succeeds with an explicit unsigned-artifact warning.

An isolated test installer uses a separate application identity and a fixed test install directory; it disables legacy-payload cleanup, shortcuts and post-install launch, and refuses installation outside that directory. Launch test applications only through the explicit isolated smoke/session helpers:

```powershell
./Build/Build_Installer.ps1 -SkipPublish -ValidationOutputRoot artifacts/review/release-validation -IsolatedInstallerValidation
./Build/Verify_Installer.ps1 -ValidationOutputRoot artifacts/review/release-validation
```

This produces `VAL_Validation_Setup_v<version>.exe` plus validation metadata under `ValidationInstallerOutput`, separately from customer installers. The verifier checks rejected directory overrides, exact installed payload hashes, installed startup, same-version repair, and uninstall/data retention. It refuses existing test installations and never invokes the production VAL uninstaller. It is not a customer release. Keep previous-version upgrade, rollback, supported-machine, and signing checks on the release checklist; passing local storage tests alone does not satisfy them.

Run source/payload compatibility and stage a separate production-installer machine kit:

```powershell
./Build/Verify_UpgradeRollback.ps1 -ValidationOutputRoot artifacts/review/v5.1.0-validation
./Build/Prepare_MachineKit.ps1 -ValidationOutputRoot artifacts/review/v5.1.0-validation
```

The compatibility matrix builds the exact tagged v5.0.0 Truth source, uses only synthetic archives, and repackages the installed v5.0.0 program payload under a disposable identity. It verifies legacy/candidate reads, duplicate recognition, rebuild backups, rollback appends, installed version/hash/file inventory, and data-retaining uninstall. It never executes the legacy desktop app on the development machine. Each run uses a new GUID directory and retains a scoped report.

The machine kit downloads the original published v5.0.0 installer and checks its recorded SHA-256, then stages it alongside the checksummed candidate. It does not run production installers here. Follow [disposable-machine instructions](../Docs/Validation/DisposableMachine.md) on a pristine throwaway Windows machine/account; existing VAL installations/data are refused. Staging is not a passing machine test.

For manual authenticated component checks, see [authenticated workflow validation](../Docs/Validation/AuthenticatedWorkflows.md). `Start_ValidationSession.ps1` opens a real interactive VAL session with isolated profile/data/configuration/state, not an automatically passing smoke test. It rejects pre-v5.1.0 executables that may ignore isolation arguments and verifies the executable checksum. Sign in manually; do not copy personal profiles or capture personal conversations. Do not upload the authenticated profile.

`Verify_BuildScripts.ps1` parses all release scripts, checks that the production-installer matrix is guarded by default, and verifies deterministic payload digests, asset-change detection and filename binding. CI also builds the locked, dependency-free archive compatibility probe; actual prior-version installers still run separately on disposable supported machines.
