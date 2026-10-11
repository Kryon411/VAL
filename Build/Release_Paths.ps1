Set-StrictMode -Version Latest

function Get-ValPayloadHash {
    param([Parameter(Mandatory)][string]$Path)
    $root = Get-Item -LiteralPath $Path -Force
    if (-not $root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Payload must be an ordinary directory.'
    }
    $records = [Collections.Generic.List[string]]::new()
    foreach ($item in Get-ChildItem -LiteralPath $root.FullName -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Payload cannot contain links.' }
        if ($item.PSIsContainer) { continue }
        $relative = [IO.Path]::GetRelativePath($root.FullName, $item.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $records.Add("$hash  $relative")
    }
    $records.Sort([StringComparer]::Ordinal)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n") + "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Assert-ValBuildDirectory {
    param([string]$RepoRoot, [string]$Path)
    $repo = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/')
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if (-not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build output must be a strict descendant of the repository: $resolved"
    }
    for ($directory = [IO.DirectoryInfo]::new($resolved); $null -ne $directory; $directory = $directory.Parent) {
        if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Build output must not traverse a junction or symbolic link: $resolved"
        }
        if ($directory.FullName.Equals($repo, [StringComparison]::OrdinalIgnoreCase)) { break }
    }
}

function Resolve-ValValidationRoot {
    param([string]$RepoRoot, [string]$RequestedRoot)
    if ([string]::IsNullOrWhiteSpace($RequestedRoot)) { return $null }
    $candidate = if ([IO.Path]::IsPathRooted($RequestedRoot)) { $RequestedRoot } else { Join-Path $RepoRoot $RequestedRoot }
    $resolved = [IO.Path]::GetFullPath($candidate).TrimEnd('\', '/')
    $artifacts = [IO.Path]::GetFullPath((Join-Path $RepoRoot "artifacts")).TrimEnd('\', '/')
    if (-not $resolved.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Validation output must be a strict descendant of artifacts."
    }
    Assert-ValBuildDirectory -RepoRoot $RepoRoot -Path $resolved
    return $resolved
}
