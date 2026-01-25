@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM Build.cmd - VAL build helper
REM ============================================================
for %%I in ("%~dp0.") do set "ROOT=%%~fI"
if /I "%~1"=="--manifest" goto :manifest
if /I "%~1"=="--publish"  goto :publish

echo.
echo Usage:
echo   Build.cmd --manifest   ^(regenerate MAIN\Manifest.txt^)
echo   Build.cmd --publish    ^(dotnet publish PRODUCT^)
echo.
exit /b 0

:manifest
echo Updating manifest...

REM Prefer PowerShell 7 if available (pwsh), otherwise fall back to Windows PowerShell (powershell).
set "PS_EXE=pwsh"
where /q pwsh || set "PS_EXE=powershell"

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\Update-Manifest.ps1" -RootPath "%ROOT%" -OutFile "%ROOT%\MAIN\Manifest.txt"
if errorlevel 1 (
  echo.
  echo ERROR: Manifest generation failed. See the error above.
  exit /b 1
)

echo Manifest updated.
exit /b 0

:publish
echo Publishing PRODUCT...

pushd "%ROOT%\MAIN" >nul
dotnet publish -c Release -o "%ROOT%\PRODUCT" || (popd & exit /b 1)
popd >nul

echo Publish complete: %ROOT%\PRODUCT
exit /b 0
