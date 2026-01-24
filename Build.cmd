@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM  VAL Build Script (clean + publish)
REM  - Publishes MAIN\VAL.csproj to PRODUCT\
REM  - Intended to be run from the repo root (this folder)
REM ============================================================

REM Always run relative to this script location
cd /d "%~dp0"

REM ---- Argument handling ---------------------------------------------------
set "MANIFEST_ONLY=0"

if /i "%~1"=="--manifest" (
  set "MANIFEST_ONLY=1"
)

if %MANIFEST_ONLY%==1 (
  echo.
  echo Updating manifest...
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Manifest.ps1" -RootPath "%~dp0" -OutFile "%~dp0MAIN\Manifest.txt"
  if %errorlevel% neq 0 (
    echo ERROR: Manifest update failed.
    exit /b 1
  )

  echo Manifest updated.
  exit /b 0
)

echo.
echo ========================================
echo           VAL BUILD (Release)
echo ========================================
echo.

REM ---- Sanity check ---------------------------------------------------------
if not exist "MAIN\VAL.csproj" (
  echo ERROR: Could not find MAIN\VAL.csproj
  echo Make sure you run this from the VAL repo root.
  pause
  exit /b 1
)

REM ---- Clean outputs --------------------------------------------------------
if exist "PRODUCT" (
  echo Removing PRODUCT...
  rmdir /s /q "PRODUCT"
)

if exist "MAIN\bin" (
  echo Removing MAIN\bin...
  rmdir /s /q "MAIN\bin"
)

if exist "MAIN\obj" (
  echo Removing MAIN\obj...
  rmdir /s /q "MAIN\obj"
)

echo.
echo Restoring...
dotnet restore "MAIN\VAL.csproj"
if %errorlevel% neq 0 (
  echo.
  echo ========================================
  echo            RESTORE FAILED
  echo ========================================
  pause
  exit /b 1
)

echo.
echo Publishing...
dotnet publish "MAIN\VAL.csproj" -c Release -o "PRODUCT"
if %errorlevel% neq 0 (
  echo.
  echo ========================================
  echo            PUBLISH FAILED
  echo ========================================
  pause
  exit /b 1
)

echo.
echo ========================================
echo          BUILD SUCCESSFUL!
echo ========================================
echo.
echo Output: %cd%\PRODUCT
echo Run:    PRODUCT\VAL.exe
echo.

echo Updating manifest...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Manifest.ps1" -RootPath "%~dp0" -OutFile "%~dp0MAIN\Manifest.txt"
if %errorlevel% neq 0 (
  echo ERROR: Manifest update failed.
  pause
  exit /b 1
)
echo Manifest updated.

pause
exit /b 0
