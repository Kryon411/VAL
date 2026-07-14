[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$FrameworkDependent,
    [string]$CertificatePath = $env:VAL_SIGNING_CERTIFICATE_PATH,
    [string]$CertificatePassword = $env:VAL_SIGNING_CERTIFICATE_PASSWORD,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "MAIN\VAL.Desktop\VAL.Desktop.csproj"
$buildPropsPath = Join-Path $repoRoot "Directory.Build.props"
$productPath = Join-Path $repoRoot "PRODUCT"
$outputPath = Join-Path $productPath "Publish"

[xml]$buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
$versionNode = $buildProps.SelectSingleNode("/Project/PropertyGroup/VersionPrefix")
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "Directory.Build.props does not define VersionPrefix."
}
$version = $versionNode.InnerText.Trim()
$archivePath = Join-Path $productPath "VAL-v$version-$RuntimeIdentifier.zip"
$symbolsArchivePath = Join-Path $productPath "VAL-v$version-$RuntimeIdentifier-symbols.zip"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Could not find MAIN\VAL.Desktop\VAL.Desktop.csproj."
}

Write-Host "Restoring locked dependencies..."
dotnet restore $projectPath --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

$resolvedProduct = [System.IO.Path]::GetFullPath($productPath)
$resolvedOutput = [System.IO.Path]::GetFullPath($outputPath)
$expectedPrefix = $resolvedProduct.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish output outside PRODUCT."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$selfContained = (-not $FrameworkDependent.IsPresent).ToString().ToLowerInvariant()
$publishArgs = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-r", $RuntimeIdentifier,
    "-o", $resolvedOutput,
    "--no-restore",
    "--self-contained", $selfContained
)
if ($FrameworkDependent.IsPresent) {
    $publishArgs += "/p:PublishSingleFile=false"
}

Write-Host "Publishing VAL..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$requiredPaths = @(
    (Join-Path $resolvedOutput "VAL.exe"),
    (Join-Path $resolvedOutput "appsettings.json"),
    (Join-Path $resolvedOutput "Modules"),
    (Join-Path $resolvedOutput "Dock"),
    (Join-Path $resolvedOutput "Icons")
)
$missing = @($requiredPaths | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) {
    throw "Publish output is missing required content:`n - $($missing -join "`n - ")"
}

$symbolPaths = @(Get-ChildItem -LiteralPath $resolvedOutput -Recurse -File -Filter "*.pdb")
if (Test-Path -LiteralPath $symbolsArchivePath) {
    Remove-Item -LiteralPath $symbolsArchivePath -Force
}
if ($symbolPaths.Count -gt 0) {
    Compress-Archive `
        -LiteralPath $symbolPaths.FullName `
        -DestinationPath $symbolsArchivePath `
        -CompressionLevel Optimal
    $symbolPaths | Remove-Item -Force
}

$executablePath = Join-Path $resolvedOutput "VAL.exe"
if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Signing certificate was not found: $CertificatePath"
    }

    Write-Host "Signing VAL.exe..."
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $CertificatePath,
        $CertificatePassword,
        $flags)
    $signature = Set-AuthenticodeSignature `
        -FilePath $executablePath `
        -Certificate $certificate `
        -TimestampServer $TimestampServer `
        -HashAlgorithm SHA256
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Code signing failed: $($signature.StatusMessage)"
    }
}

$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content `
    -LiteralPath (Join-Path $resolvedOutput "VAL.exe.sha256") `
    -Value "$hash  VAL.exe" `
    -Encoding ASCII

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive `
    -Path (Join-Path $resolvedOutput "*") `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

Write-Host "Publish successful."
Write-Host "Output:  $resolvedOutput"
Write-Host "Archive: $archivePath"
if (Test-Path -LiteralPath $symbolsArchivePath) {
    Write-Host "Symbols: $symbolsArchivePath"
}
Write-Host "Run:     $resolvedOutput\VAL.exe"
