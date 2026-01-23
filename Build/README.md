# Release publishing

From the repo root:

```powershell
.\Build\Publish_Release.ps1
```

The script restores and publishes `MAIN/VAL.csproj` to `PRODUCT/Publish`, verifying required runtime content (`appsettings.json`, `Modules`, `Dock`, `Icons`).

Optional:

```powershell
.\Build\Publish_Release.ps1 -RuntimeIdentifier win-x64 -SelfContained
```

Smoke test example:

```powershell
VAL.exe --smoke --smoke-timeout-ms=20000
```
