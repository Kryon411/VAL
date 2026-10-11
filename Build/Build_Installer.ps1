[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipPublish,
    [string]$ValidationOutputRoot,
    [switch]$IsolatedInstallerValidation,
    [string]$CompilerPath = $env:INNO_SETUP_COMPILER,
    [string]$CertificatePath = $env:VAL_SIGNING_CERTIFICATE_PATH,
    [string]$CertificatePassword = $env:VAL_SIGNING_CERTIFICATE_PASSWORD,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Release_Paths.ps1")
$validationRoot = Resolve-ValValidationRoot -RepoRoot $repoRoot -RequestedRoot $ValidationOutputRoot
if ($IsolatedInstallerValidation -and $null -eq $validationRoot) {
    throw "An isolated validation installer requires ValidationOutputRoot under artifacts."
}
$buildPropsPath = Join-Path $repoRoot "Directory.Build.props"
$installerScript = Join-Path $repoRoot "Installer\VAL.iss"
$publishScript = Join-Path $PSScriptRoot "Publish_Release.ps1"
$publishPath = if ($null -eq $validationRoot) { Join-Path $repoRoot "PRODUCT\Publish" } else { Join-Path $validationRoot "Publish" }
$installerOutput = if ($null -eq $validationRoot) { Join-Path $repoRoot "InstallerOutput" }
    elseif ($IsolatedInstallerValidation) { Join-Path $validationRoot "ValidationInstallerOutput" }
    else { Join-Path $validationRoot "InstallerOutput" }

[xml]$buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
$versionNodes = @(
    $buildProps.Project.PropertyGroup |
        ForEach-Object { $_.SelectSingleNode("VersionPrefix") } |
        Where-Object { $null -ne $_ }
)
if ($versionNodes.Count -ne 1) {
    throw "Directory.Build.props must define exactly one VersionPrefix."
}

$version = $versionNodes[0].InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Installer releases require a three-part numeric VersionPrefix; found '$version'."
}

$installerSource = Get-Content -LiteralPath $installerScript -Raw
$installerVersion = [regex]::Match($installerSource, '(?m)^#define AppVersion\s+"([^"]+)"$')
if (-not $installerVersion.Success -or $installerVersion.Groups[1].Value -ne $version) {
    throw "Installer\VAL.iss AppVersion must match VersionPrefix $version."
}

if (-not $SkipPublish.IsPresent) {
    $publishParameters = @{ RuntimeIdentifier = $RuntimeIdentifier }
    if ($null -ne $validationRoot) { $publishParameters.ValidationOutputRoot = $validationRoot }
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $publishParameters.CertificatePath = $CertificatePath
        $publishParameters.CertificatePassword = $CertificatePassword
        $publishParameters.TimestampServer = $TimestampServer
    }

    & $publishScript @publishParameters
    if ($LASTEXITCODE -ne 0) { throw "Release publish failed." }
}

$executablePath = Join-Path $publishPath "VAL.exe"
Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $publishPath
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable was not found: $executablePath"
}
$expectedExecutableHash = (Get-Content -LiteralPath "$executablePath.sha256" -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
if ((Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash -ne $expectedExecutableHash) {
    throw "Published executable checksum mismatch. Republish before building the installer."
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath).FileVersion
$payloadHash = Get-ValPayloadHash -Path $publishPath
$expectedFileVersion = "$version.0"
if ($fileVersion -ne $expectedFileVersion) {
    throw "Published VAL.exe has file version '$fileVersion'; expected '$expectedFileVersion'."
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $CompilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($CompilerPath) -or
    -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw "Inno Setup 6 compiler was not found. Install Inno Setup or set INNO_SETUP_COMPILER."
}

$resolvedOutput = [System.IO.Path]::GetFullPath($installerOutput)
Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $resolvedOutput
$expectedOutput = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($expectedOutput, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean installer output outside the repository."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

Write-Host "Compiling VAL v$version installer..."
$baseName = "VAL_Setup_v$version"
$compilerArguments = @("/O$resolvedOutput", "/DPayloadDir=$publishPath")
if ($IsolatedInstallerValidation) {
    $baseName = "VAL_Validation_Setup_v$version"
    $validationAppId = [Guid]::NewGuid().ToString('N')
    $installRoot = Join-Path $validationRoot "Installed"
    if ($installRoot.Contains("'")) { throw "Validation install path cannot contain an apostrophe." }
    Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $installRoot
    $compilerArguments += @("/DValidationBuild", "/DValidationAppId=$validationAppId", "/DValidationInstallRoot=$installRoot")
}
$compilerArguments += @("/F$baseName", $installerScript)
& $CompilerPath @compilerArguments | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$installerPath = Join-Path $resolvedOutput "$baseName.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer was not produced: $installerPath"
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Signing certificate was not found: $CertificatePath"
    }

    Write-Host "Signing installer..."
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $CertificatePath,
        $CertificatePassword,
        $flags)
    $signature = Set-AuthenticodeSignature `
        -FilePath $installerPath `
        -Certificate $certificate `
        -TimestampServer $TimestampServer `
        -HashAlgorithm SHA256
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Installer signing failed: $($signature.StatusMessage)"
    }
}
else {
    Write-Warning "No signing certificate was configured; the installer is unsigned."
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$installerPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$hash  $(Split-Path -Leaf $installerPath)" -Encoding ASCII
if ((Get-ValPayloadHash -Path $publishPath) -ne $payloadHash) { throw 'Published payload changed while compiling the installer.' }
if ($IsolatedInstallerValidation) {
    @{ AppId = "VAL.Validation.$validationAppId"; InstallRoot = $installRoot; InstallerPath = $installerPath; PayloadSha256 = $payloadHash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutput "validation-installer.json") -Encoding utf8
}
@{ ExecutableSha256 = $expectedExecutableHash; PayloadSha256 = $payloadHash; InstallerSha256 = $hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutput 'installer-build.json') -Encoding utf8

Write-Host "Installer build successful."
Write-Host "Installer: $installerPath"
Write-Host "Checksum:  $hashPath"
