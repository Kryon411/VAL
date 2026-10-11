[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$files = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File) +
    @(Get-Item -LiteralPath (Join-Path $repoRoot 'Update-Manifest.ps1'))
foreach ($file in $files) {
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$null, [ref]$parseErrors)
    if ($parseErrors.Count) { throw "Invalid script $($file.Name): $($parseErrors.Message -join '; ')" }
}
$refused = $false
try { & (Join-Path $PSScriptRoot 'Verify_DisposableMachine.ps1') -KitRoot (Join-Path $repoRoot 'artifacts\nonexistent-kit') }
catch {
    if ($_.Exception.Message -notlike 'This test installs and rolls back production VAL.*') { throw }
    $refused = $true
}
if (-not $refused) { throw 'Production installer matrix did not refuse a missing disposable-machine acknowledgement.' }
. (Join-Path $PSScriptRoot 'Release_Paths.ps1')
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('VAL.BuildScripts\' + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'VAL.exe') -Value 'synthetic executable fixture' -Encoding ascii
    $asset = Join-Path $fixtureRoot 'Client.js'
    Set-Content -LiteralPath $asset -Value 'synthetic client fixture' -Encoding ascii
    $first = Get-ValPayloadHash -Path $fixtureRoot
    if ((Get-ValPayloadHash -Path $fixtureRoot) -ne $first) { throw 'Payload digest is not deterministic.' }
    Set-Content -LiteralPath $asset -Value 'updated synthetic client fixture' -Encoding ascii
    $updated = Get-ValPayloadHash -Path $fixtureRoot
    if ($updated -eq $first) { throw 'Payload digest did not detect an asset-only change.' }
    Move-Item -LiteralPath $asset -Destination (Join-Path $fixtureRoot 'Renamed.js')
    if ((Get-ValPayloadHash -Path $fixtureRoot) -eq $updated) { throw 'Payload digest did not bind relative filenames.' }
}
finally {
    $prefix = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'VAL.BuildScripts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $fixtureRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($fixtureRoot) -notmatch '^[a-f0-9]{32}$') { throw 'Unsafe build-script fixture cleanup path.' }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
Write-Host "$($files.Count) build scripts parse; production-installer guard and three payload-digest checks passed."
