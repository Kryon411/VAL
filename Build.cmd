@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM Build.cmd - VAL build helper
REM Commands:
REM   Build.cmd --test
REM   Build.cmd --manifest
REM   Build.cmd --publish         (DEFAULT: self-contained win-x64)
REM   Build.cmd --publish-sc      (self-contained win-x64)
REM   Build.cmd --publish-fdd     (framework-dependent; disables single-file compression)
REM   Build.cmd --release         (test + manifest + publish)
REM ============================================================

for %%I in ("%~dp0.") do set "ROOT=%%~fI"
set "CONFIG=Release"
set "TEST_PROJ=%ROOT%\MAIN\VAL.Tests\VAL.Tests.csproj"

if /I "%~1"=="--test"        goto :test
if /I "%~1"=="--manifest"    goto :manifest
if /I "%~1"=="--publish"     goto :publish_sc
if /I "%~1"=="--publish-sc"  goto :publish_sc
if /I "%~1"=="--publish-fdd" goto :publish_fdd
if /I "%~1"=="--release"     goto :release

echo.
echo Usage:
echo   Build.cmd --test
echo   Build.cmd --manifest
echo   Build.cmd --publish         ^(default: self-contained win-x64^)
echo   Build.cmd --publish-sc      ^(self-contained win-x64^)
echo   Build.cmd --publish-fdd     ^(framework-dependent^)
echo   Build.cmd --release         ^(test + manifest + publish^)
echo.
exit /b 0

:test
echo Running tests (%CONFIG%)...
dotnet test "%TEST_PROJ%" -c %CONFIG%
if errorlevel 1 (
  echo.
  echo ERROR: Tests failed.
  exit /b 1
)
echo Tests passed.
exit /b 0

:manifest
echo Updating manifest...

REM Prefer PowerShell 7 if available (pwsh), otherwise fall back to Windows PowerShell (powershell).
set "PS_EXE=pwsh"
where /q pwsh || set "PS_EXE=powershell"

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\Update-Manifest.ps1" -RootPath "%ROOT%" -OutFile "%ROOT%\MAIN\Manifest.txt"
if errorlevel 1 (
  echo.
  echo ERROR: Manifest generation failed.
  exit /b 1
)

echo Manifest updated.
exit /b 0

:publish_fdd
set "PS_EXE=pwsh"
where /q pwsh || set "PS_EXE=powershell"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\Build\Publish_Release.ps1" -FrameworkDependent
if errorlevel 1 (
  echo.
  echo ERROR: Publish failed.
  exit /b 1
)
exit /b 0

:publish_sc
set "PS_EXE=pwsh"
where /q pwsh || set "PS_EXE=powershell"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\Build\Publish_Release.ps1"
if errorlevel 1 (
  echo.
  echo ERROR: Publish failed.
  exit /b 1
)
exit /b 0

:release
call "%~f0" --test || exit /b 1
call "%~f0" --manifest || exit /b 1
call "%~f0" --publish || exit /b 1
echo.
echo RELEASE pipeline complete.
exit /b 0
