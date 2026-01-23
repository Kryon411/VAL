@echo off
setlocal EnableExtensions

set SCRIPT_DIR=%~dp0
set POWERSHELL_EXE=powershell

%POWERSHELL_EXE% -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Publish_Release.ps1" %*
if %errorlevel% neq 0 (
  exit /b %errorlevel%
)
