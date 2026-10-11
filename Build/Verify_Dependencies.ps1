[CmdletBinding()]
param(
    [string]$DotnetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "VAL.sln"
if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $localSdk = Join-Path $repoRoot 'artifacts\tools\dotnet\dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $localSdk -PathType Leaf) { $localSdk } else { 'dotnet' }
}
$json = & $DotnetPath list $solution package --vulnerable --include-transitive --no-restore --format json
if ($LASTEXITCODE -ne 0) { throw "Dependency audit failed to run." }
$report = ($json -join "`n") | ConvertFrom-Json
if ($report.version -ne 1 -or $null -eq $report.PSObject.Properties['projects']) {
    throw "Unsupported dependency audit report. Refusing to treat it as a clean audit."
}
$expectedProjects = @([regex]::Matches((Get-Content -LiteralPath $solution -Raw), '"([^"\r\n]+\.csproj)"') |
    ForEach-Object { [IO.Path]::GetFullPath((Join-Path $repoRoot $_.Groups[1].Value)) })
$reportedProjects = @($report.projects | ForEach-Object { [IO.Path]::GetFullPath($_.path) })
if ($reportedProjects.Count -ne $expectedProjects.Count -or @(Compare-Object $expectedProjects $reportedProjects).Count) {
    throw 'Dependency audit did not report every solution project. Refusing a partial clean result.'
}

$findings = @(
    foreach ($project in $report.projects) {
        $frameworks = $project.PSObject.Properties['frameworks']
        if ($null -eq $frameworks) { continue }
        foreach ($framework in $frameworks.Value) {
            foreach ($group in @('topLevelPackages', 'transitivePackages')) {
                $packages = $framework.PSObject.Properties[$group]
                if ($null -eq $packages) { continue }
                foreach ($package in $packages.Value) {
                    $vulnerabilities = $package.PSObject.Properties['vulnerabilities']
                    if ($null -eq $vulnerabilities) { continue }
                    foreach ($vulnerability in $vulnerabilities.Value) {
                        "$($package.id) $($package.resolvedVersion): $($vulnerability.severity) $($vulnerability.advisoryurl) ($($project.path))"
                    }
                }
            }
        }
    }
)
if ($findings.Count -gt 0) {
    $findings | Write-Host
    throw "Dependency audit found $($findings.Count) vulnerable package advisory entries."
}
Write-Host "No known vulnerable packages were reported across $(@($report.projects).Count) projects."
