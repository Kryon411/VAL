# Release Publishing

From the repository root:

```powershell
./Build/Publish_Release.ps1
```

The script publishes `MAIN/VAL.Desktop/VAL.Desktop.csproj` to `PRODUCT/Publish`, verifies required runtime content, writes `VAL.exe.sha256`, and creates versioned application and private support-symbol archives under `PRODUCT`.

Use `-FrameworkDependent` only for internal diagnostics. Set `VAL_SIGNING_CERTIFICATE_PATH` and `VAL_SIGNING_CERTIFICATE_PASSWORD` to sign release candidates.

Run the published smoke test with:

```powershell
./PRODUCT/Publish/VAL.exe --smoke --smoke-timeout-ms=30000
```

Build the versioned per-user installer with Inno Setup 6:

```powershell
./Build/Build_Installer.ps1
```

The installer script is tracked at `Installer/VAL.iss`. The builder verifies that its version matches `Directory.Build.props`, publishes VAL, validates `VAL.exe` version metadata, compiles `InstallerOutput/VAL_Setup_v<version>.exe`, and writes a SHA-256 checksum. Set `INNO_SETUP_COMPILER` when `ISCC.exe` is not installed in a standard location.

Set `VAL_SIGNING_CERTIFICATE_PATH` and `VAL_SIGNING_CERTIFICATE_PASSWORD` to timestamp-sign both `VAL.exe` and the installer. Without these values, the build succeeds with an explicit unsigned-artifact warning.
