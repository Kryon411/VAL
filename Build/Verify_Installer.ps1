#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ValidationOutputRoot,
    [int]$TimeoutMs = 90000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Release_Paths.ps1")
$root = Resolve-ValValidationRoot -RepoRoot $repoRoot -RequestedRoot $ValidationOutputRoot
$validationReport = Join-Path $root "installer-validation.json"
if (Test-Path -LiteralPath $validationReport) { Remove-Item -LiteralPath $validationReport -Force }
$output = Join-Path $root "ValidationInstallerOutput"
$metadata = Get-Content -LiteralPath (Join-Path $output "validation-installer.json") -Raw | ConvertFrom-Json
if ($metadata.AppId -notmatch '^VAL\.Validation\.[a-f0-9]{32}$') { throw "Only isolated validation identities are allowed." }
$installRoot = [IO.Path]::GetFullPath((Join-Path $root "Installed"))
$installer = [IO.Path]::GetFullPath($metadata.InstallerPath)
if ([IO.Path]::GetFullPath($metadata.InstallRoot) -ne $installRoot -or
    [IO.Path]::GetDirectoryName($installer) -ne $output -or
    [IO.Path]::GetFileName($installer) -notmatch '^VAL_Validation_Setup_v\d+\.\d+\.\d+\.exe$') {
    throw "Validation metadata does not match the fixed test directories."
}
Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $installRoot
Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $output
if (Test-Path -LiteralPath $installRoot) { throw "Test install directory must not already exist; existing contents will not be deleted." }
if (@(Get-Process -Name VAL -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "Close VAL before installer validation. No existing process will be terminated."
}
$registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($metadata.AppId)_is1"
if (Test-Path -LiteralPath $registryPath) { throw "The validation application is already registered." }
$expectedHash = (Get-Content -LiteralPath "$installer.sha256" -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) { throw "Installer checksum mismatch." }
$payloadHash = Get-ValPayloadHash -Path (Join-Path $root 'Publish')
if ($metadata.PayloadSha256 -ne $payloadHash) { throw 'Installer was built from a different published payload.' }
$checks = [Collections.Generic.List[string]]::new()
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) ("VAL.Smoke\" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$preservedFixture = Join-Path $dataRoot "uninstall-retention-fixture.txt"
Set-Content -LiteralPath $preservedFixture -Value "Synthetic user data must survive application uninstall." -Encoding utf8

function Invoke-OwnedValidationProcess {
    param([string]$FilePath, [string[]]$Arguments, [string]$Name, [int]$ExpectedExitCode = 0)
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit($TimeoutMs)) {
            $process.Kill($true)
            throw "Owned validation process timed out: $Name"
        }
        if ($process.ExitCode -ne $ExpectedExitCode) { throw "$Name returned $($process.ExitCode), expected $ExpectedExitCode." }
    }
    finally { $process.Dispose() }
}

function Assert-InstalledPayload {
    foreach ($source in Get-ChildItem -LiteralPath (Join-Path $root "Publish") -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath((Join-Path $root "Publish"), $source.FullName)
        $installed = Join-Path $installRoot $relative
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf) -or
            (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash) {
            throw "Installed payload mismatch: $relative"
        }
    }
    $registration = Get-ItemProperty -LiteralPath $registryPath
    if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installRoot) {
        throw "Validation uninstall registration points outside the test directory."
    }
}

$commonArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=', '/NOCLOSEAPPLICATIONS', '/NOFORCECLOSEAPPLICATIONS')
$rejectedRoot = Join-Path $root "Rejected-Install-Target"
if (Test-Path -LiteralPath $rejectedRoot) { throw "Rejected test target already exists." }
Invoke-OwnedValidationProcess -FilePath $installer -Name "Rejected directory override" -ExpectedExitCode 7 -Arguments (
    $commonArguments + @("/DIR=`"$rejectedRoot`"", "/LOG=`"$(Join-Path $root 'installer-rejected.log')`""))
if ((Test-Path -LiteralPath $rejectedRoot) -or (Test-Path -LiteralPath $registryPath)) {
    throw "The rejected directory override created an installation."
}
$checks.Add("Installer refuses a directory override before writing payload or registration")

$uninstallCompleted = $false
try {
    Invoke-OwnedValidationProcess -FilePath $installer -Name "Isolated install" -Arguments (
        $commonArguments + @("/LOG=`"$(Join-Path $root 'installer-install.log')`""))
    Assert-InstalledPayload
    $checks.Add("Per-user install has exact payload hashes and isolated uninstall registration")

    $report = Join-Path $root "installed-smoke.txt"
    Invoke-OwnedValidationProcess -FilePath (Join-Path $installRoot "VAL.exe") -Name "Installed smoke" -Arguments @(
        '--smoke', '--smoke-timeout-ms=30000', "--smoke-data-root=`"$dataRoot`"", "--smoke-report-path=`"$report`""
    )
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw "Installed smoke did not produce its report." }
    Get-Content -LiteralPath $report | Write-Host
    $checks.Add("Installed executable passes fresh-process startup smoke")

    Set-Content -LiteralPath (Join-Path $installRoot "appsettings.json") -Value "Synthetic damaged payload for repair verification." -Encoding utf8
    Invoke-OwnedValidationProcess -FilePath $installer -Name "Same-version repair" -Arguments (
        $commonArguments + @("/LOG=`"$(Join-Path $root 'installer-repair.log')`""))
    Assert-InstalledPayload
    $checks.Add("Same-version reinstall repairs a damaged payload and preserves synthetic user data")
}
finally {
    if (Test-Path -LiteralPath $registryPath) {
        Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $installRoot
        $registration = Get-ItemProperty -LiteralPath $registryPath
        if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installRoot) {
            throw "Refusing uninstall because the registered path changed."
        }
        $uninstaller = Join-Path $installRoot "unins000.exe"
        if ($registration.UninstallString.Trim('"') -ne $uninstaller -or
            -not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
            throw "Refusing an unexpected validation uninstaller."
        }
        Invoke-OwnedValidationProcess -FilePath $uninstaller -Name "Isolated uninstall" -Arguments @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $root 'installer-uninstall.log')`""
        )
        $uninstallCompleted = $true
    }
}

if (-not $uninstallCompleted -or (Test-Path -LiteralPath $registryPath) -or
    (Test-Path -LiteralPath (Join-Path $installRoot "VAL.exe")) -or
    -not (Test-Path -LiteralPath $preservedFixture)) { throw "Uninstall did not remove the test application while preserving synthetic user data." }
$checks.Add("Uninstall removes the isolated application and registration without deleting synthetic user data")
@{
    Passed = $true
    CheckedAtUtc = [DateTime]::UtcNow.ToString('O')
    AppId = $metadata.AppId
    InstallRoot = $installRoot
    DataRoot = $dataRoot
    InstallerSha256 = $expectedHash
    PayloadSha256 = $payloadHash
    AuthenticodeStatus = (Get-AuthenticodeSignature -LiteralPath $installer).Status.ToString()
    Checks = $checks.ToArray()
    Scope = 'Isolated current-version lifecycle only; prior-version upgrade/rollback and supported-machine validation remain required.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $validationReport -Encoding utf8
Write-Host "$($checks.Count) isolated installer-validation checks passed."
