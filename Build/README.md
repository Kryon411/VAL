# Release Publishing

From the repository root:

```powershell
./Build/Publish_Release.ps1
```

The script publishes `MAIN/VAL.Desktop/VAL.Desktop.csproj` to `PRODUCT/Publish`, verifies required runtime content, writes `VAL.exe.sha256`, and creates `PRODUCT/VAL-win-x64.zip`.

Use `-FrameworkDependent` only for internal diagnostics. Set `VAL_SIGNING_CERTIFICATE_PATH` and `VAL_SIGNING_CERTIFICATE_PASSWORD` to sign release candidates.

Run the published smoke test with:

```powershell
./PRODUCT/Publish/VAL.exe --smoke --smoke-timeout-ms=30000
```
