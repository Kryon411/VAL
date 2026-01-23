param(
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "MAIN\VAL.csproj"
$outputPath = Join-Path $repoRoot "PRODUCT\Publish"

if (-not (Test-Path $projectPath)) {
    Write-Error "Could not find MAIN\VAL.csproj. Run from the repo root."
}

Write-Host ""
Write-Host "========================================"
Write-Host "        VAL RELEASE PUBLISH"
Write-Host "========================================"
Write-Host ""

Write-Host "Restoring..."
dotnet restore $projectPath

Write-Host ""
Write-Host "Publishing..."
$publishArgs = @(
    $projectPath,
    "-c", "Release",
    "-r", $RuntimeIdentifier,
    "-o", $outputPath
)

if ($SelfContained.IsPresent) {
    $publishArgs += "--self-contained"
}

dotnet publish @publishArgs

Write-Host ""
Write-Host "Verifying required content..."
$requiredPaths = @(
    (Join-Path $outputPath "appsettings.json"),
    (Join-Path $outputPath "appsettings.Development.json"),
    (Join-Path $outputPath "Modules"),
    (Join-Path $outputPath "Dock"),
    (Join-Path $outputPath "Icons")
)

$missing = @()
foreach ($path in $requiredPaths) {
    if (-not (Test-Path $path)) {
        $missing += $path
    }
}

if ($missing.Count -gt 0) {
    Write-Error ("Publish output is missing required content:`n - " + ($missing -join "`n - "))
}

Write-Host ""
Write-Host "========================================"
Write-Host "         PUBLISH SUCCESSFUL!"
Write-Host "========================================"
Write-Host ""
Write-Host "Output: $outputPath"
Write-Host "Run:    $outputPath\VAL.exe"
