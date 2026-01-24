param(
    [string]$RootPath = $PSScriptRoot,
    [string]$OutFile = "$PSScriptRoot\\MAIN\\Manifest.txt"
)

$resolvedRoot = (Resolve-Path -Path $RootPath).Path

$excludedDirs = @(
    '.git',
    '.vs',
    'bin',
    'obj',
    'node_modules',
    'InstallerOutput',
    'Artifacts'
)

function Get-SortedDirectories {
    param([string]$Path)

    Get-ChildItem -LiteralPath $Path -Directory -Force |
        Where-Object { $excludedDirs -notcontains $_.Name } |
        Sort-Object @{ Expression = { $_.Name.ToLowerInvariant() } }
}

function Get-SortedFiles {
    param([string]$Path)

    Get-ChildItem -LiteralPath $Path -File -Force |
        Sort-Object @{ Expression = { $_.Name.ToLowerInvariant() } }
}

function Add-TreeLines {
    param(
        [string]$Path,
        [string]$Indent,
        [ref]$Lines
    )

    foreach ($directory in (Get-SortedDirectories -Path $Path)) {
        $Lines.Value += "${Indent}${directory.Name}/"
        Add-TreeLines -Path $directory.FullName -Indent "${Indent}  " -Lines $Lines
    }

    foreach ($file in (Get-SortedFiles -Path $Path)) {
        $Lines.Value += "${Indent}${file.Name}"
    }
}

function Get-AllFiles {
    param(
        [string]$Path,
        [ref]$Files
    )

    foreach ($file in (Get-SortedFiles -Path $Path)) {
        $Files.Value += $file.FullName
    }

    foreach ($directory in (Get-SortedDirectories -Path $Path)) {
        Get-AllFiles -Path $directory.FullName -Files $Files
    }
}

$treeLines = @()
Add-TreeLines -Path $resolvedRoot -Indent '' -Lines ([ref]$treeLines)

$allFiles = @()
Get-AllFiles -Path $resolvedRoot -Files ([ref]$allFiles)
$sortedFiles = $allFiles | Sort-Object @{ Expression = { $_.ToLowerInvariant() } }

$nl = "`r`n"
$lines = @(
    '===== TREE MANIFEST =====',
    "Root: $resolvedRoot",
    "Generated: $(Get-Date -Format s)",
    ''
)

$lines += $treeLines
$lines += ''
$lines += '===== FULL PATH MANIFEST ====='
$lines += ''
$lines += $sortedFiles

$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutFile, ($lines -join $nl), $encoding)
