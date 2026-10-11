#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ValidationOutputRoot,
    [int]$SmokeTimeoutMs = 30000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Release_Paths.ps1")
$root = Resolve-ValValidationRoot -RepoRoot $repoRoot -RequestedRoot $ValidationOutputRoot
$validationReport = Join-Path $root "published-validation.json"
if (Test-Path -LiteralPath $validationReport) { Remove-Item -LiteralPath $validationReport -Force }
$publish = Join-Path $root "Publish"
$exe = Join-Path $publish "VAL.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published VAL.exe was not found." }
if (@(Get-Process -Name VAL -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "Close the running VAL instance before validation. No existing process will be terminated."
}
foreach ($required in @("appsettings.json", "Modules", "Dock", "Icons", "VAL.exe.sha256")) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) { throw "Missing published content: $required" }
}
if (@(Get-ChildItem -LiteralPath $publish -Recurse -File -Filter '*.pdb').Count -gt 0) {
    throw "Debug symbols must be packaged separately from the customer payload."
}
$expectedHash = (Get-Content -LiteralPath (Join-Path $publish "VAL.exe.sha256") -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $expectedHash) { throw "Executable checksum mismatch." }
$payloadHash = Get-ValPayloadHash -Path $publish

$checks = [Collections.Generic.List[string]]::new()
$checks.Add("Published content, symbol separation, and executable checksum")
function Invoke-ValSmoke {
    param([string]$DataRoot, [string]$Name, [int]$ExpectedExitCode = 0)
    $report = Join-Path $root "$Name-smoke.txt"
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    $process = Start-Process -FilePath $exe -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -ArgumentList @(
        '--smoke', "--smoke-timeout-ms=$SmokeTimeoutMs", "--smoke-data-root=`"$DataRoot`"", "--smoke-report-path=`"$report`""
    )
    try {
        if (-not $process.WaitForExit($SmokeTimeoutMs + 30000)) {
            $process.Kill($true)
            throw "Owned validation process timed out: $Name"
        }
        if ($process.ExitCode -ne $ExpectedExitCode) {
            if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report | Write-Host }
            throw "$Name returned $($process.ExitCode), expected $ExpectedExitCode."
        }
        if ($ExpectedExitCode -eq 0) {
            if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw "Smoke report was not produced: $Name" }
            if ((Get-Content -LiteralPath $report -Raw) -notmatch '(?m)^NativeThemeReady: True\r?$') {
                throw "Native shell theme did not pass fresh-process validation: $Name"
            }
            Get-Content -LiteralPath $report | Write-Host
        }
        elseif (Test-Path -LiteralPath $report) { throw "Blocked cleanup unexpectedly reached runtime smoke: $Name" }
    }
    finally { $process.Dispose() }
}
function New-ValidationDataRoot {
    $path = Join-Path ([IO.Path]::GetTempPath()) ("VAL.Smoke\" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}
function Write-PendingWipe {
    param([string]$DataRoot, [string[]]$Targets, $Browser = $null)
    $state = Join-Path $DataRoot "State"
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    @{ Version = 1; DataRoot = $DataRoot; Targets = $Targets; Browser = $Browser } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $state "pending-data-wipe.json") -Encoding utf8
}

$dataRoot = New-ValidationDataRoot
Invoke-ValSmoke -DataRoot $dataRoot -Name "initial"
$initialReport = Get-Content -LiteralPath (Join-Path $root "initial-smoke.txt") -Raw
$browserId = [regex]::Match($initialReport, '(?m)^BrowserProcessId: (\d+)').Groups[1].Value
$browserStart = [regex]::Match($initialReport, '(?m)^BrowserStartedUtcTicks: (\d+)').Groups[1].Value
$browser = if ($browserId -and $browserId -ne '0') { @{ Id = [int]$browserId; StartedUtcTicks = [long]$browserStart } } else { $null }
$checks.Add("Fresh-process published smoke including resolved native shell colors, typography, and recovery-button template")
$targets = @('Logs', 'Profile', 'Snapshots', 'Staging', 'Memory\Chats') | ForEach-Object { Join-Path $dataRoot $_ }
foreach ($target in $targets) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $target "private-validation.txt") -Value "Synthetic validation data" -Encoding utf8
}
$settingsPath = Join-Path $dataRoot "settings.json"
'{"version":1,"continuumLoggingEnabled":false,"portalCaptureEnabled":false}' |
    Set-Content -LiteralPath $settingsPath -Encoding utf8
$configPath = Join-Path $dataRoot "config.json"
'{"Val":{"EnableVerboseLogging":false}}' | Set-Content -LiteralPath $configPath -Encoding utf8
Write-PendingWipe -DataRoot $dataRoot -Targets $targets -Browser $browser
$pending = Join-Path $dataRoot "State\pending-data-wipe.json"
$archive = Join-Path $dataRoot "Memory\Chats\private-validation.txt"
$locked = [IO.File]::Open($archive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Invoke-ValSmoke -DataRoot $dataRoot -Name "locked-cleanup" -ExpectedExitCode 41
        $startup = Get-Content -LiteralPath (Join-Path $dataRoot "State\startup.json") -Raw | ConvertFrom-Json
        if ($startup.consecutiveStartupCrashes -ne 0) { throw "Expected cleanup failure was recorded as a startup crash." }
    }
    if (-not (Test-Path -LiteralPath $pending) -or -not (Test-Path -LiteralPath $archive)) {
        throw "Locked cleanup did not preserve the pending request and locked archive."
    }
}
finally { $locked.Dispose() }
$checks.Add("Repeated locked cleanup fails closed, retains retry request, and does not accumulate startup crashes")
Invoke-ValSmoke -DataRoot $dataRoot -Name "cleanup-retry"
foreach ($target in $targets) {
    if (Test-Path -LiteralPath (Join-Path $target "private-validation.txt")) { throw "Deleted private fixture was recreated: $target" }
}
if (Test-Path -LiteralPath $pending) { throw "Completed cleanup left a pending request." }
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($settings.continuumLoggingEnabled -or $settings.portalCaptureEnabled -or -not (Test-Path -LiteralPath $configPath)) {
    throw "Cleanup did not preserve preferences and disabled capture."
}
$checks.Add("Fresh-process cleanup retry deletes all synthetic data and preserves disabled preferences")

$invalidRoot = New-ValidationDataRoot
Write-PendingWipe -DataRoot $invalidRoot -Targets @()
Invoke-ValSmoke -DataRoot $invalidRoot -Name "invalid-cleanup" -ExpectedExitCode 41
if (Test-Path -LiteralPath (Join-Path $invalidRoot "Profile")) { throw "Invalid cleanup request started the browser." }
$checks.Add("Invalid cleanup request blocks runtime startup")

$invalidPathRoot = New-ValidationDataRoot
New-Item -ItemType Directory -Path (Join-Path $invalidPathRoot "State\pending-data-wipe.json") -Force | Out-Null
Invoke-ValSmoke -DataRoot $invalidPathRoot -Name "invalid-cleanup-path" -ExpectedExitCode 41
if (Test-Path -LiteralPath (Join-Path $invalidPathRoot "Profile")) { throw "Invalid cleanup path started the browser." }
$checks.Add("Invalid cleanup path is not mistaken for a missing request")

if ((Get-ValPayloadHash -Path $publish) -ne $payloadHash) { throw 'Published payload changed during validation.' }
@{
    Passed = $true
    CheckedAtUtc = [DateTime]::UtcNow.ToString('O')
    ExecutableSha256 = $expectedHash
    PayloadSha256 = $payloadHash
    DataRoots = @($dataRoot, $invalidRoot, $invalidPathRoot)
    Checks = $checks.ToArray()
    AuthenticodeStatus = (Get-AuthenticodeSignature -LiteralPath $exe).Status.ToString()
    Scope = 'Local process/storage integration and smoke only; not authenticated-browser or supported-machine certification.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $validationReport -Encoding utf8
Write-Host "$($checks.Count) published release-validation checks passed."
