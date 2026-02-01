# Build Smoke Checklist

- [ ] Confirm .NET SDK 8.x is available (`dotnet --info`) and matches `global.json`.
- [ ] Confirm WebView2 Runtime is installed on the test machine.
- [ ] `dotnet build MAIN/VAL.csproj` succeeds with no errors.
- [ ] (Optional) `Build.cmd --publish` completes and creates the `PRODUCT/` folder.
- [ ] Launch `dotnet run --project MAIN/VAL.csproj` or `MAIN/bin/Debug/net8.0-windows/VAL.exe`.
- [ ] App window renders and loads the start URL.
- [ ] Open the Control Centre (dock/pill UI) and confirm it is responsive.
- [ ] Toggle a module action (e.g., Continuum quick refresh) to verify command routing works.
- [ ] Confirm `%LOCALAPPDATA%\VAL` is created and contains `Logs/VAL.log`.
- [ ] Restart the app to ensure it launches cleanly a second time.
