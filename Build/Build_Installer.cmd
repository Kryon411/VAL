@echo off
setlocal
set "PS_EXE=pwsh"
where /q pwsh || set "PS_EXE=powershell"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build_Installer.ps1" %*
exit /b %errorlevel%
