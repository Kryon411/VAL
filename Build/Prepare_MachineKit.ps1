#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ValidationOutputRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Release_Paths.ps1')
$root = Resolve-ValValidationRoot -RepoRoot $repoRoot -RequestedRoot $ValidationOutputRoot
$kit = Join-Path $root ('MachineKit\' + [Guid]::NewGuid().ToString('N'))
Assert-ValBuildDirectory -RepoRoot $repoRoot -Path $kit
New-Item -ItemType Directory -Path $kit -Force | Out-Null
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
$candidate = Join-Path $root "InstallerOutput\VAL_Setup_v$version.exe"
$candidateExe = Join-Path $root 'Publish\VAL.exe'
$payloadHash = Get-ValPayloadHash -Path (Join-Path $root 'Publish')
$build = Get-Content -LiteralPath (Join-Path $root 'InstallerOutput\installer-build.json') -Raw | ConvertFrom-Json
if ($build.PayloadSha256 -ne $payloadHash -or $build.InstallerSha256 -ne (Get-FileHash -LiteralPath $candidate).Hash) {
    throw 'Customer installer does not match the current published payload.'
}
foreach ($file in @($candidate, $candidateExe)) {
    $expected = (Get-Content -LiteralPath "$file.sha256" -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
    if ((Get-FileHash -LiteralPath $file).Hash -ne $expected) { throw 'Candidate checksum mismatch.' }
}
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($candidateExe).FileVersion -ne "$version.0") { throw 'Candidate executable version mismatch.' }
$baselineHash = '04c55345dd0275c51bf4b9a152febc0b2f68656cb47eff87b90568da3b0f154a'
$baseline = Join-Path $kit 'VAL_Setup_v5.0.0.exe'
Invoke-WebRequest -Uri 'https://github.com/Kryon411/VAL/releases/download/v5.0.0/VAL_Setup_v5.0.0.exe' -OutFile $baseline
if ((Get-FileHash -LiteralPath $baseline).Hash -ne $baselineHash) { throw 'The original published v5.0.0 installer does not match its recorded release digest.' }
Copy-Item -LiteralPath $candidate, "$candidate.sha256", (Join-Path $PSScriptRoot 'Verify_DisposableMachine.ps1') -Destination $kit
Copy-Item -LiteralPath (Join-Path $repoRoot 'Docs\Validation\DisposableMachine.md') -Destination (Join-Path $kit 'README.md')
@{
    PreparedAtUtc = [DateTime]::UtcNow.ToString('O')
    BaselineVersion = '5.0.0'
    CandidateVersion = $version
    BaselineInstallerSha256 = $baselineHash
    CandidateInstallerSha256 = (Get-FileHash -LiteralPath $candidate).Hash
    BaselineExecutableSha256 = '61fc1b42359448a963de1cb676723a9070615b140281d76e69b3882777868ed1'
    CandidateExecutableSha256 = (Get-FileHash -LiteralPath $candidateExe).Hash
    CandidatePayloadSha256 = $payloadHash
    CandidateSignature = (Get-AuthenticodeSignature -LiteralPath $candidate).Status.ToString()
    Scope = 'Staged only. No production installer was run on the development machine.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $kit 'machine-kit.json') -Encoding utf8
Write-Host "Disposable-machine kit staged (not executed): $kit"
