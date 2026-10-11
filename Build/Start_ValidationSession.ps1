[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ValidationOutputRoot,
    [string]$SessionRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Release_Paths.ps1")
$root = Resolve-ValValidationRoot -RepoRoot $repoRoot -RequestedRoot $ValidationOutputRoot
$exe = Join-Path $root "Publish\VAL.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published validation executable was not found." }
$version = [Version]([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion)
if ($version -lt [Version]'5.1.0.0') { throw "Interactive isolation requires VAL 5.1.0 or later. Older executables may ignore the validation arguments." }
$expectedHash = (Get-Content -LiteralPath "$exe.sha256" -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $expectedHash) { throw "Published executable checksum mismatch." }
if (@(Get-Process -Name VAL -ErrorAction SilentlyContinue).Count -gt 0) { throw "Close VAL before starting an isolated validation session." }
$parent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "VAL.Validation"))
$dataRoot = [IO.Path]::GetFullPath($(if ([string]::IsNullOrWhiteSpace($SessionRoot)) {
    Join-Path $parent ([Guid]::NewGuid().ToString('N'))
} else { $SessionRoot }))
$leafGuid = [Guid]::Empty
if ([IO.Path]::GetDirectoryName($dataRoot) -ne $parent -or
    -not [Guid]::TryParseExact([IO.Path]::GetFileName($dataRoot), 'N', [ref]$leafGuid)) {
    throw "Interactive validation data must be a GUID directory directly under Temp\VAL.Validation."
}
for ($directory = [IO.DirectoryInfo]::new($dataRoot); $null -ne $directory; $directory = $directory.Parent) {
    if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Validation paths cannot traverse directory links." }
}
if (-not (Test-Path -LiteralPath $dataRoot)) {
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    '{"version":1,"continuumLoggingEnabled":false,"portalCaptureEnabled":false}' |
        Set-Content -LiteralPath (Join-Path $dataRoot "settings.json") -Encoding utf8
}
$process = Start-Process -FilePath $exe -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -ArgumentList @(
    '--validation-session', "--validation-data-root=`"$dataRoot`""
)
@{
    StartedAtUtc = [DateTime]::UtcNow.ToString('O')
    ProcessId = $process.Id
    Executable = $exe
    DataRoot = $dataRoot
    Scope = 'Interactive session; not an automatic pass. Profile may contain authentication data after manual sign-in.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root "interactive-session.json") -Encoding utf8
Write-Host "Isolated VAL session started (PID $($process.Id))."
Write-Host "Data and profile: $dataRoot"
Write-Host "New sessions start with capture disabled; reopened sessions keep their settings. Sign in manually if needed; use only synthetic test conversations."
$process.Dispose()
