#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ValidationOutputRoot,
    [string]$BaselinePayload = (Join-Path $env:LOCALAPPDATA 'Programs\VAL'),
    [string]$CompilerPath = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    [int]$TimeoutMs = 120000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Release_Paths.ps1')
$root = Resolve-ValValidationRoot -RepoRoot $repoRoot -RequestedRoot $ValidationOutputRoot
$candidate = Join-Path $root 'Publish'
$runRoot = Join-Path $root ('UpgradeRollback\' + [Guid]::NewGuid().ToString('N'))
Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $runRoot
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$baselineExe = Join-Path $BaselinePayload 'VAL.exe'
$candidateExe = Join-Path $candidate 'VAL.exe'
$baselineVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($baselineExe).FileVersion
$candidateVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidateExe).FileVersion
if ($baselineVersion -ne '5.0.0.0' -or [Version]$candidateVersion -le [Version]$baselineVersion) {
    throw 'This matrix requires a v5.0.0 baseline and a later candidate.'
}
foreach ($exe in @($baselineExe, $candidateExe)) {
    $expected = (Get-Content -LiteralPath "$exe.sha256" -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $expected) { throw "Payload checksum mismatch: $exe" }
}
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Inno Setup compiler was not found.' }
$dotnet = Join-Path $repoRoot 'artifacts\tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$checks = [Collections.Generic.List[string]]::new()

function Invoke-CheckedTool {
    param([string]$Tool, [string[]]$Arguments)
    & $Tool @Arguments | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Tool returned $LASTEXITCODE : $Tool" }
}
function Invoke-OwnedInstaller {
    param([string]$FilePath, [string[]]$Arguments)
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit($TimeoutMs)) { $process.Kill($true); throw 'Owned isolated installer timed out.' }
        if ($process.ExitCode -ne 0) { throw "Isolated installer returned $($process.ExitCode)." }
    } finally { $process.Dispose() }
}

# Build the exact tagged archive implementation, not the old desktop executable.
$source = Join-Path $runRoot 'LegacySource'
$archive = Join-Path $runRoot 'legacy-source.zip'
$baselineCommit = (& git -C $repoRoot rev-parse 'v5.0.0^{commit}').Trim()
if ($LASTEXITCODE -ne 0 -or $baselineCommit -ne '1d8d835aa9f03ec5c3ac4163b5f236d895511e40') { throw 'The v5.0.0 source tag is not the expected released revision.' }
Invoke-CheckedTool git @('-C', $repoRoot, 'archive', '--format=zip', "--output=$archive", 'v5.0.0', 'Directory.Build.props', 'MAIN/VAL.Truth')
Expand-Archive -LiteralPath $archive -DestinationPath $source
$legacyProject = Join-Path $source 'MAIN\VAL.Truth\VAL.Truth.csproj'
Invoke-CheckedTool $dotnet @('build', $legacyProject, '-c', 'Release', '--nologo', '-p:RestoreLockedMode=true')
Invoke-CheckedTool $dotnet @('build', (Join-Path $PSScriptRoot 'UpgradeProbe\UpgradeProbe.csproj'), '-c', 'Release', '--nologo', '-p:RestoreLockedMode=true')
$probe = Join-Path $PSScriptRoot 'UpgradeProbe\bin\Release\net10.0\UpgradeProbe.dll'
$legacyTruth = Join-Path $source 'MAIN\VAL.Truth\bin\Release\net8.0\VAL.Truth.dll'
$currentTruth = Join-Path $repoRoot 'MAIN\VAL.Truth\bin\Release\net10.0\VAL.Truth.dll'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($currentTruth).FileVersion -ne $candidateVersion) { throw 'Build the current solution before running this matrix.' }
$dataRoot = Join-Path $runRoot 'SyntheticData'
$chats = Join-Path $dataRoot 'Memory\Chats'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$prefs = Join-Path $dataRoot 'settings.json'
'{"version":1,"continuumLoggingEnabled":false,"portalCaptureEnabled":false}' | Set-Content -LiteralPath $prefs -Encoding utf8
$prefsHash = (Get-FileHash -LiteralPath $prefs).Hash
foreach ($step in @(@($legacyTruth, 'seed'), @($currentTruth, 'upgrade'), @($legacyTruth, 'rollback'), @($currentTruth, 'verify'))) {
    Invoke-CheckedTool $dotnet @($probe, $step[0], $chats, $step[1])
    $checks.Add("Archive compatibility: $($step[1])")
}
$truthFixture = Join-Path $chats 'synthetic-upgrade-fixture\Truth.log'
$truthHash = (Get-FileHash -LiteralPath $truthFixture).Hash

# Repackage known payloads with one disposable identity; never run legacy VAL.exe.
$baselineCopy = Join-Path $runRoot 'BaselinePayload'
New-Item -ItemType Directory -Path $baselineCopy -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $BaselinePayload -Force) {
    if ($item.Name -notin @('VAL.exe', 'VAL.exe.sha256', 'appsettings.json', 'Dock', 'Icons', 'Modules')) { continue }
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Baseline payload cannot contain links.' }
    if ($item.PSIsContainer -and @(Get-ChildItem -LiteralPath $item.FullName -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Baseline payload cannot contain nested links.' }
    Copy-Item -LiteralPath $item.FullName -Destination $baselineCopy -Recurse
}
$appId = 'VAL.Validation.' + [Guid]::NewGuid().ToString('N')
$installRoot = Join-Path $runRoot 'Installed'
$registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath $registryPath)) { throw 'Disposable installation must not already exist.' }
if ($installRoot.Contains("'")) { throw 'Validation install path cannot contain an apostrophe.' }
$installers = @{}
foreach ($payload in @(@($baselineCopy, '5.0.0'), @($candidate, ([Version]$candidateVersion).ToString(3)))) {
    $output = Join-Path $runRoot ('Installer-' + $payload[1])
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Invoke-CheckedTool $CompilerPath @("/O$output", "/FVAL_Validation_Setup_v$($payload[1])", "/DPayloadDir=$($payload[0])", "/DAppVersion=$($payload[1])", '/DValidationBuild', "/DValidationAppId=$($appId.Substring('VAL.Validation.'.Length))", "/DValidationInstallRoot=$installRoot", (Join-Path $repoRoot 'Installer\VAL.iss'))
    $installers[$payload[1]] = Join-Path $output "VAL_Validation_Setup_v$($payload[1]).exe"
}
function Assert-InstalledVersion {
    param([string]$Payload, [string]$Version)
    Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $installRoot
    $registration = Get-ItemProperty -LiteralPath $registryPath
    if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installRoot -or $registration.DisplayVersion -ne $Version) { throw 'Unexpected installation registration.' }
    $payloadFiles = @(Get-ChildItem -LiteralPath $Payload -Recurse -File)
    foreach ($file in $payloadFiles) {
        $installed = Join-Path $installRoot ([IO.Path]::GetRelativePath($Payload, $file.FullName))
        if ((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installed payload mismatch: $installed" }
    }
    $expectedFiles = @($payloadFiles | ForEach-Object { [IO.Path]::GetRelativePath($Payload, $_.FullName) })
    $actualFiles = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File | Where-Object { $_.Name -notmatch '^unins\d+\.(exe|dat|msg)$' } | ForEach-Object { [IO.Path]::GetRelativePath($installRoot, $_.FullName) })
    $differences = @(Compare-Object $expectedFiles $actualFiles)
    if ($differences.Count) { throw "Installed payload includes missing or obsolete files: $($differences | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" } | Join-String -Separator '; ')" }
    if ((Get-FileHash -LiteralPath $prefs).Hash -ne $prefsHash -or (Get-FileHash -LiteralPath $truthFixture).Hash -ne $truthHash) { throw 'Synthetic preferences or archives changed during installation.' }
}
function Uninstall-OwnedValidation {
    param([string]$LogName)
    Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $installRoot
    $registration = Get-ItemProperty -LiteralPath $registryPath
    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installRoot -or $registration.UninstallString.Trim('"') -ne $uninstaller) { throw 'Refusing an unexpected validation uninstaller.' }
    Invoke-OwnedInstaller $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $runRoot $LogName)`"")
    # Inno's self-removal helper can finish after the parent process exits.
    $deadline = [DateTime]::UtcNow.AddMilliseconds([Math]::Min($TimeoutMs, 10000))
    do {
        $cleanupPending = (Test-Path -LiteralPath $registryPath) -or @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($cleanupPending) { Start-Sleep -Milliseconds 100 }
    } while ($cleanupPending -and [DateTime]::UtcNow -lt $deadline)
    if ((Test-Path -LiteralPath $registryPath) -or @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -ErrorAction SilentlyContinue).Count) { throw 'Candidate uninstall left application files or registration behind.' }
    if ((Get-FileHash -LiteralPath $prefs).Hash -ne $prefsHash -or (Get-FileHash -LiteralPath $truthFixture).Hash -ne $truthHash) { throw 'Uninstall changed synthetic preferences or archives.' }
}
$common = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=', '/NOCLOSEAPPLICATIONS', '/NOFORCECLOSEAPPLICATIONS')
try {
    foreach ($step in @(@($baselineCopy, '5.0.0', 'baseline'), @($candidate, ([Version]$candidateVersion).ToString(3), 'upgrade'), @($baselineCopy, '5.0.0', 'rollback'))) {
        if ($step[2] -eq 'rollback') {
            # Old installers cannot remove assets introduced by a later release.
            Uninstall-OwnedValidation 'candidate-uninstall-before-rollback.log'
            $checks.Add('Rollback preparation removes candidate assets and registration while retaining synthetic data')
        }
        Invoke-OwnedInstaller $installers[$step[1]] ($common + @("/LOG=`"$(Join-Path $runRoot ($step[2] + '.log'))`""))
        Assert-InstalledVersion $step[0] $step[1]
        $checks.Add("Isolated payload lifecycle: $($step[2]) with exact hashes and version registration")
    }
} finally {
    if (Test-Path -LiteralPath $registryPath) {
        Uninstall-OwnedValidation 'uninstall.log'
    }
}
if ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath (Join-Path $installRoot 'VAL.exe')) -or (Get-FileHash -LiteralPath $prefs).Hash -ne $prefsHash) { throw 'Isolated uninstall or retention failed.' }
Invoke-CheckedTool $dotnet @($probe, $currentTruth, $chats, 'verify')
$checks.Add('Isolated uninstall retains readable synthetic archives and unchanged preferences')
@{
    Passed = $true
    CheckedAtUtc = [DateTime]::UtcNow.ToString('O')
    BaselineCommit = $baselineCommit
    BaselineVersion = $baselineVersion
    CandidateVersion = $candidateVersion
    BaselineExecutableSha256 = (Get-FileHash -LiteralPath $baselineExe).Hash
    CandidateExecutableSha256 = (Get-FileHash -LiteralPath $candidateExe).Hash
    BaselinePayloadSha256 = Get-ValPayloadHash -Path $baselineCopy
    CandidatePayloadSha256 = Get-ValPayloadHash -Path $candidate
    InstallerSha256 = @{
        Baseline = (Get-FileHash -LiteralPath $installers['5.0.0']).Hash
        Candidate = (Get-FileHash -LiteralPath $installers[([Version]$candidateVersion).ToString(3)]).Hash
    }
    AppId = $appId
    Checks = $checks.ToArray()
    Scope = 'Exact tagged Truth source compatibility and repackaged isolated payload upgrade/rollback. Rollback uninstalls the candidate while retaining data before reinstalling the baseline. Does not run the legacy desktop executable or original production installers. A disposable supported-machine matrix is still required.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'upgrade-rollback-validation.json') -Encoding utf8
Write-Host "$($checks.Count) upgrade/rollback checks passed. Report: $runRoot"
