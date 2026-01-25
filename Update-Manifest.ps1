<#
  Update-Manifest.ps1

  Generates MAIN\Manifest.txt for the repo.

  Key goals:
  - Works when called from Build.cmd OR directly from PowerShell
  - Tolerates accidental stray quotes/control chars in inputs
  - Avoids .NET Path.IsPathRooted throwing on "junk" input
  - Produces stable, forward-slashed relative paths in the manifest
#>

[CmdletBinding()]
param(
  [string] $RootPath = "",
  [string] $OutFile  = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-SafeScriptDir {
  if ($PSScriptRoot) { return $PSScriptRoot }
  # PS2/odd invocation fallback
  if ($MyInvocation.MyCommand.Path) { return (Split-Path -Parent $MyInvocation.MyCommand.Path) }
  return (Get-Location).Path
}

function Sanitize-PathString([string] $s) {
  if ($null -eq $s) { return "" }
  $t = $s.Trim()

  # Strip wrapping quotes if they match.
  if (($t.Length -ge 2) -and (
      ($t.StartsWith('"') -and $t.EndsWith('"')) -or
      ($t.StartsWith("'") -and $t.EndsWith("'"))
    )) {
    $t = $t.Substring(1, $t.Length - 2)
  }

  # Remove any stray quote characters (quotes are invalid in Windows paths).
  $t = ($t -replace "[`"']", "").Trim()

  # Remove ASCII/unicode control chars (including stray CR/LF, etc.).
  $chars = New-Object System.Collections.Generic.List[char]
  foreach ($ch in $t.ToCharArray()) {
    if (-not [char]::IsControl($ch)) { [void]$chars.Add($ch) }
  }
  $t = -join $chars

  return $t.Trim()
}

function Try-SplitRootAndOutFileFromRootPath {
  # If RootPath accidentally contains "-OutFile ..." (usually from a quoting mistake in a caller),
  # try to recover both values without failing the whole script.
  param(
    [string] $rootPathMaybe,
    [ref]    $rootOut,
    [ref]    $outOut
  )

  $rootPathMaybe = Sanitize-PathString $rootPathMaybe
  if ([string]::IsNullOrWhiteSpace($rootPathMaybe)) { return $false }

  if ($rootPathMaybe -notmatch '\s+-OutFile\s+') { return $false }

  $m = [regex]::Match($rootPathMaybe, '^\s*(?<root>.+?)\s+-OutFile\s+(?<out>.+?)\s*$')
  if (-not $m.Success) { return $false }

  $rootOut.Value = (Sanitize-PathString $m.Groups['root'].Value)
  $outOut.Value  = (Sanitize-PathString $m.Groups['out'].Value)
  return (-not [string]::IsNullOrWhiteSpace($rootOut.Value))
}

function Test-RootedWindows([string] $p) {
  $p = Sanitize-PathString $p
  if ([string]::IsNullOrWhiteSpace($p)) { return $false }
  return ($p -match '^[A-Za-z]:[\\/]' -or $p -match '^\\\\')
}

function Resolve-FullPath([string] $p, [string] $baseDir) {
  $p = Sanitize-PathString $p
  $baseDir = Sanitize-PathString $baseDir

  if ([string]::IsNullOrWhiteSpace($p)) { return "" }
  if ([string]::IsNullOrWhiteSpace($baseDir)) { $baseDir = (Get-Location).Path }

  if (-not (Test-RootedWindows $p)) {
    $p = Join-Path $baseDir $p
  }

  try {
    return [System.IO.Path]::GetFullPath($p)
  } catch {
    $chars = ($p.ToCharArray() | ForEach-Object { '0x{0:X4}' -f [int]$_ }) -join ' '
    throw "Failed to normalize path input: [$p] (Exception calling GetFullPath: $($_.Exception.Message)). Char codes: $chars"
  }
}

function To-RelSlashPath([string] $fullPath, [string] $rootFull) {
  $fullPath = Resolve-FullPath $fullPath $rootFull
  $rootFull = Resolve-FullPath $rootFull $rootFull

  if ([string]::IsNullOrWhiteSpace($fullPath) -or [string]::IsNullOrWhiteSpace($rootFull)) { return "" }

  # Ensure trailing slash on root for consistent rel.
  $rootWithSep = $rootFull.TrimEnd('\','/') + '\'

  # Case-insensitive on Windows.
  if ($fullPath.Length -lt $rootWithSep.Length -or ($fullPath.Substring(0, $rootWithSep.Length).ToLowerInvariant() -ne $rootWithSep.ToLowerInvariant())) {
    throw "Path is outside root. Root=[$rootFull] Path=[$fullPath]"
  }

  $rel = $fullPath.Substring($rootWithSep.Length)
  return ($rel -replace '\\','/')
}

try {
  $scriptDir = Get-SafeScriptDir

  # If RootPath is missing, default to the script directory (repo root when script lives at repo root).
  if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = $scriptDir
  }

  # Recovery: if RootPath accidentally swallowed "-OutFile ..." and OutFile is empty, split them.
  if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $rootRecovered = ""
    $outRecovered  = ""
    if (Try-SplitRootAndOutFileFromRootPath -rootPathMaybe $RootPath -rootOut ([ref]$rootRecovered) -outOut ([ref]$outRecovered)) {
      $RootPath = $rootRecovered
      if (-not [string]::IsNullOrWhiteSpace($outRecovered)) { $OutFile = $outRecovered }
    }
  }

  $rootFull = Resolve-FullPath $RootPath $scriptDir
  if (-not (Test-Path -LiteralPath $rootFull -PathType Container)) {
    throw "RootPath does not exist or is not a directory: $rootFull"
  }

  # OutFile defaults to ROOT\MAIN\Manifest.txt if omitted.
  if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $rootFull "MAIN\Manifest.txt"
  }
  $outFull = Resolve-FullPath $OutFile $rootFull
  if ([string]::IsNullOrWhiteSpace($outFull)) {
    throw "OutFile resolved to an empty path."
  }

  $outDir = Split-Path -Parent $outFull
  if ([string]::IsNullOrWhiteSpace($outDir)) {
    throw "Could not determine output directory for OutFile: $outFull"
  }
  if (-not (Test-Path -LiteralPath $outDir -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
  }

  # Collect file list (repo-root relative). Keep predictable ordering.
  $files =
    Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force |
    Where-Object {
      $p = $_.FullName

      # Exclude common noise
      if ($p -match '\\\.git\\') { return $false }
      if ($p -match '\\bin\\') { return $false }
      if ($p -match '\\obj\\') { return $false }

      return $true
    } |
    ForEach-Object { To-RelSlashPath $_.FullName $rootFull } |
    Sort-Object

  # Write manifest
  Set-Content -LiteralPath $outFull -Value $files -Encoding UTF8
  Write-Host "Manifest written: $outFull"
  exit 0
}
catch {
  Write-Error $_
  exit 1
}
