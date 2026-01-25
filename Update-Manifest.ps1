param(
    [string]$RootPath = $PSScriptRoot,
    [string]$OutFile = "$PSScriptRoot\MAIN\Manifest.txt",
    [switch]$IncludeGenerated
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Normalize paths
$rootFull = (Resolve-Path -LiteralPath $RootPath).Path
$outFull  = (Resolve-Path -LiteralPath (Split-Path -Parent $OutFile) -ErrorAction SilentlyContinue)
if (-not $outFull) {
    # Create the parent dir if missing
    $parent = Split-Path -Parent $OutFile
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
}
$outFull = (Resolve-Path -LiteralPath (Split-Path -Parent $OutFile)).Path
$outFull = Join-Path $outFull (Split-Path -Leaf $OutFile)

# Exclusions (skip common build noise and generated outputs)
$excludedDirs = @(
    ".git", ".github", ".vs",
    "bin", "obj", "node_modules",
    "InstallerOutput", "Artifacts", "artifacts",
    ".artifacts", ".cache", ".tmp", "tmp"
)

function Should-ExcludePath([string]$fullPath) {
    foreach ($d in $excludedDirs) {
        $needle = [System.IO.Path]::DirectorySeparatorChar + $d + [System.IO.Path]::DirectorySeparatorChar
        if ($fullPath.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
        # Also exclude if the path ends exactly with "\<dir>"
        $endNeedle = [System.IO.Path]::DirectorySeparatorChar + $d
        if ($fullPath.EndsWith($endNeedle, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# Walk files deterministically
$files = Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force -ErrorAction Stop |
    Where-Object { -not (Should-ExcludePath $_.FullName) }

$treeLines = $files |
    ForEach-Object {
        $rel = $_.FullName.Substring($rootFull.Length).TrimStart('\','/')
        $rel -replace '\\','/'
    } |
    Sort-Object { $_.ToLowerInvariant() }

$fullLines = $files |
    ForEach-Object { $_.FullName } |
    Sort-Object { $_.ToLowerInvariant() }

# Build manifest content (always overwrites)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# VAL MANIFEST")
[void]$sb.AppendLine("# Root: $rootFull")
[void]$sb.AppendLine("# GeneratedBy: Update-Manifest.ps1")
if ($IncludeGenerated) {
    # NOTE: This makes the file change every run. Use only when you WANT that.
    [void]$sb.AppendLine("# GeneratedAtUtc: $(Get-Date -AsUTC -Format 'yyyy-MM-ddTHH:mm:ssZ')")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("=== TREE MANIFEST ===")
foreach ($l in $treeLines) { [void]$sb.AppendLine($l) }
[void]$sb.AppendLine("")
[void]$sb.AppendLine("=== FULL PATH MANIFEST ===")
foreach ($l in $fullLines) { [void]$sb.AppendLine($l) }

# Normalize CRLF and write UTF-8 (no BOM) deterministically
$content = ($sb.ToString() -replace "`r?`n", "`r`n")
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outFull, $content, $utf8NoBom)
