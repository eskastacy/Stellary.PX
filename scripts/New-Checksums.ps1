#Requires -Version 5.1
<#
.SYNOPSIS
    Writes a sha256sum-style manifest for every file under a directory.

.DESCRIPTION
    Paths are relative and use forward slashes. The manifest itself is excluded.
    Output is UTF-8 without a BOM so standard tools can parse it.

.PARAMETER Path
    Directory to hash.

.PARAMETER Output
    Manifest path. Defaults to <Path>\SHA256SUMS.txt.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$Output
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Not a directory: $root"
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'SHA256SUMS.txt'
}
$Output = [System.IO.Path]::GetFullPath($Output)
$rootFull = [System.IO.Path]::GetFullPath($root).TrimEnd('\', '/')

$files = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.FullName -ne $Output -and
        $_.Name -ne 'SHA256SUMS.txt'
    } |
    Sort-Object FullName

if (-not $files) {
    throw "No files found under $root"
}

$lines = foreach ($file in $files) {
    $relative = $file.FullName.Substring($rootFull.Length).TrimStart('\', '/') -replace '\\', '/'
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($Output, [string[]]$lines, $utf8)

Write-Host ("Wrote {0} entries to {1}" -f @($lines).Count, $Output)
