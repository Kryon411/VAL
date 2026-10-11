#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KitRoot,
    [switch]$ConfirmDisposableMachine,
    [int]$TimeoutMs = 120000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableMachine) { throw 'This test installs and rolls back production VAL. Use only a disposable Windows VM, then explicitly pass ConfirmDisposableMachine.' }
$kit = [IO.Path]::GetFullPath($KitRoot)
$manifest = Get-Content -LiteralPath (Join-Path $kit 'machine-kit.json') -Raw | ConvertFrom-Json
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\VAL'
$dataRoot = Join-Path $env:LOCALAPPDATA 'VAL'
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{d11fe636-f181-4cdf-bc7e-e4adcfe52237}_is1'
if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath $dataRoot) -or (Test-Path -LiteralPath $registryPath) -or
    @(Get-Process -Name VAL -ErrorAction SilentlyContinue).Count) {
    throw 'A pristine disposable user account is required. Existing VAL files, data, registrations, or processes will not be overwritten.'
}
if ($manifest.BaselineVersion -ne '5.0.0' -or [Version]$manifest.CandidateVersion -le [Version]'5.0.0') { throw 'Unexpected kit versions.' }
$baseline = Join-Path $kit 'VAL_Setup_v5.0.0.exe'
$candidate = Join-Path $kit "VAL_Setup_v$($manifest.CandidateVersion).exe"
foreach ($pair in @(@($baseline, $manifest.BaselineInstallerSha256), @($candidate, $manifest.CandidateInstallerSha256))) {
    if ((Get-FileHash -LiteralPath $pair[0]).Hash -ne $pair[1]) { throw 'Installer kit checksum mismatch.' }
}
$results = Join-Path $kit ('Results-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $results | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Invoke-OwnedProcess {
    param([string]$Exe, [string[]]$Arguments)
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit($TimeoutMs)) { $process.Kill($true); throw 'Owned validation process timed out.' }
        if ($process.ExitCode -ne 0) { throw "Validation process returned $($process.ExitCode): $Exe" }
    } finally { $process.Dispose() }
}
function Assert-InstalledVersion {
    param([string]$Version, [string]$Hash)
    $registration = Get-ItemProperty -LiteralPath $registryPath
    if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installRoot -or $registration.DisplayVersion -ne $Version) { throw 'Unexpected production uninstall registration.' }
    if ((Get-FileHash -LiteralPath (Join-Path $installRoot 'VAL.exe')).Hash -ne $Hash) { throw 'Unexpected installed executable.' }
}
function Uninstall-OwnedProduction {
    param([string]$LogName)
    $registration = Get-ItemProperty -LiteralPath $registryPath
    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installRoot -or $registration.UninstallString.Trim('"') -ne $uninstaller) { throw 'Refusing an unexpected production uninstaller.' }
    for ($directory = [IO.DirectoryInfo]::new($installRoot); $null -ne $directory; $directory = $directory.Parent) {
        if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Refusing uninstall through directory links.' }
    }
    Invoke-OwnedProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $results $LogName)`"")
    # Wait for the uninstaller's separate self-removal helper, not an arbitrary delay.
    $deadline = [DateTime]::UtcNow.AddMilliseconds([Math]::Min($TimeoutMs, 10000))
    do {
        $cleanupPending = (Test-Path -LiteralPath $registryPath) -or @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($cleanupPending) { Start-Sleep -Milliseconds 100 }
    } while ($cleanupPending -and [DateTime]::UtcNow -lt $deadline)
    if ((Test-Path -LiteralPath $registryPath) -or @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -ErrorAction SilentlyContinue).Count) { throw 'Production uninstall left application files or registration behind.' }
    foreach ($file in $fixtureHashes.Keys) { if ((Get-FileHash -LiteralPath $file).Hash -ne $fixtureHashes[$file]) { throw 'Production uninstall changed synthetic user data.' } }
}
$common = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=', '/NOCLOSEAPPLICATIONS', '/NOFORCECLOSEAPPLICATIONS')
$fixtureHashes = @{}
$baselineFileHashes = @{}
try {
    Invoke-OwnedProcess $baseline ($common + @("/LOG=`"$(Join-Path $results 'baseline-install.log')`""))
    Assert-InstalledVersion '5.0.0' $manifest.BaselineExecutableSha256
    foreach ($file in Get-ChildItem -LiteralPath $installRoot -Recurse -File | Where-Object { $_.Name -notmatch '^unins\d+\.(exe|dat|msg)$' }) {
        $baselineFileHashes[[IO.Path]::GetRelativePath($installRoot, $file.FullName)] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    $checks.Add('Original v5.0.0 production installer: clean install, executable hash and stable registration')
    $chatRoot = Join-Path $dataRoot 'Memory\Chats\synthetic-machine-fixture'
    New-Item -ItemType Directory -Path $chatRoot -Force | Out-Null
    $fixture = Join-Path $chatRoot 'Truth.log'
    "U|Synthetic machine validation prompt.`nA|Synthetic machine validation response." | Set-Content -LiteralPath $fixture -Encoding utf8
    $settings = Join-Path $dataRoot 'settings.json'
    '{"version":1,"continuumLoggingEnabled":false,"portalCaptureEnabled":false}' | Set-Content -LiteralPath $settings -Encoding utf8
    foreach ($file in @($fixture, $settings)) { $fixtureHashes[$file] = (Get-FileHash -LiteralPath $file).Hash }
    foreach ($step in @(@($candidate, $manifest.CandidateVersion, $manifest.CandidateExecutableSha256, 'upgrade'), @($baseline, '5.0.0', $manifest.BaselineExecutableSha256, 'rollback'))) {
        if ($step[3] -eq 'rollback') {
            Uninstall-OwnedProduction 'candidate-uninstall-before-rollback.log'
            $checks.Add('Rollback preparation removes candidate assets and registration while retaining synthetic data')
        }
        Invoke-OwnedProcess $step[0] ($common + @("/LOG=`"$(Join-Path $results ($step[3] + '.log'))`""))
        Assert-InstalledVersion $step[1] $step[2]
        if ($step[3] -eq 'rollback') {
            $actualFiles = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File | Where-Object { $_.Name -notmatch '^unins\d+\.(exe|dat|msg)$' })
            if (@(Compare-Object @($baselineFileHashes.Keys) @($actualFiles | ForEach-Object { [IO.Path]::GetRelativePath($installRoot, $_.FullName) })).Count) { throw 'Rollback left missing or obsolete application assets.' }
            foreach ($file in $actualFiles) {
                if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne $baselineFileHashes[[IO.Path]::GetRelativePath($installRoot, $file.FullName)]) { throw 'Rollback did not restore the original baseline payload.' }
            }
        }
        foreach ($file in $fixtureHashes.Keys) { if ((Get-FileHash -LiteralPath $file).Hash -ne $fixtureHashes[$file]) { throw 'Production installer changed synthetic user data.' } }
        $checks.Add("Original production installer: $($step[3]) with expected executable, registration and unchanged synthetic user data")
    }
} finally {
    if (Test-Path -LiteralPath $registryPath) {
        Uninstall-OwnedProduction 'uninstall.log'
    }
}
if ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath (Join-Path $installRoot 'VAL.exe'))) { throw 'Production uninstall did not complete.' }
foreach ($file in $fixtureHashes.Keys) { if ((Get-FileHash -LiteralPath $file).Hash -ne $fixtureHashes[$file]) { throw 'Production uninstall changed synthetic user data.' } }
$checks.Add('Original production uninstaller removes the application and registration but retains synthetic settings and archives')
@{
    Passed = $true
    CheckedAtUtc = [DateTime]::UtcNow.ToString('O')
    WindowsVersion = [Environment]::OSVersion.VersionString
    Is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
    Kit = $manifest
    Checks = $checks.ToArray()
    Scope = 'Production installer lifecycle on an explicitly disposable pristine account. Does not validate authenticated browser workflows or desktop runtime rollback; run those separately before discarding the VM.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'production-installer-validation.json') -Encoding utf8
Write-Host "$($checks.Count) production-installer checks passed. Results: $results"
