# Build Smoke Checklist

- [ ] `dotnet restore VAL.sln --locked-mode` succeeds.
- [ ] `dotnet format VAL.sln --no-restore --verify-no-changes` succeeds.
- [ ] `dotnet build VAL.sln -c Release --no-restore` succeeds without warnings.
- [ ] `dotnet test MAIN/VAL.Tests/VAL.Tests.csproj -c Release --no-build --no-restore` passes.
- [ ] `./Build/Publish_Release.ps1` produces `PRODUCT/Publish/VAL.exe`, its checksum, and the ZIP artifact.
- [ ] `PRODUCT/Publish/VAL.exe --smoke --smoke-timeout-ms=30000` succeeds.
- [ ] A normal launch renders the ChatGPT page and responsive Control Centre.
- [ ] Continuum Pulse, Chronicle cancellation, Abyss search/inject, Portal, and privacy controls are exercised manually.
- [ ] `%LOCALAPPDATA%\VAL` contains logs and data; a second launch does not enter safe mode.
- [ ] Signed release candidates report a valid timestamped Authenticode signature.
