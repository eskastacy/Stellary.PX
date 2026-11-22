#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies files against a SHA256SUMS manifest and optionally requires a valid
    Authenticode signature.

.EXAMPLE
    .\scripts\Verify-Release.ps1 -Path .\dist\Stellary.PX-0.1.2-win-x64 -Sums .\dist\Stellary.PX-0.1.2-win-x64\SHA256SUMS.txt

.EXAMPLE
    .\scripts\Verify-Release.ps1 -Path .\Stellary.PX.exe -Sums .\SHA256SUMS.txt -RequireSignature
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$Sums,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'

$target = (Resolve-Path -LiteralPath $Path).Path
$manifest = (Resolve-Path -LiteralPath $Sums).Path
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "Manifest not found: $manifest"
}

$verifySingleFile = Test-Path -LiteralPath $target -PathType Leaf
$root = if ($verifySingleFile) { Split-Path -Parent $target } else { $target.TrimEnd('\', '/') }
$targetName = if ($verifySingleFile) { Split-Path -Leaf $target } else { $null }

$entries = @()
foreach ($line in Get-Content -LiteralPath $manifest) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }
    $parts = $trimmed -split '\s+', 2
    if ($parts.Count -lt 2) { continue }
    $listed = $parts[0]
    $relative = $parts[1].TrimStart('*').Trim() -replace '/', '\'
    if ($verifySingleFile -and (Split-Path -Leaf $relative) -ne $targetName) { continue }
    $entries += [pscustomobject]@{ Hash = $listed; Relative = $relative }
}

if (-not $entries) {
    throw "No manifest entries match $target"
}

$verified = 0
$mismatched = 0
$missing = 0

foreach ($entry in $entries) {
    $file = if ($verifySingleFile) { $target } else { Join-Path $root $entry.Relative }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        Write-Warning "MISSING  $($entry.Relative)"
        $missing++
        continue
    }

    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -eq $entry.Hash.ToLowerInvariant()) {
        Write-Host "OK       $($entry.Relative)"
        $verified++
    }
    else {
        Write-Warning "MISMATCH $($entry.Relative)"
        Write-Host "         expected $($entry.Hash.ToLowerInvariant())"
        Write-Host "         actual   $actual"
        $mismatched++
    }
}

Write-Host ''
Write-Host ("verified {0} | mismatched {1} | missing {2}" -f $verified, $mismatched, $missing)

$exe = if ($verifySingleFile) { $target } elseif (Test-Path (Join-Path $root 'Stellary.PX.exe')) { Join-Path $root 'Stellary.PX.exe' } else { $null }
if ($exe) {
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    Write-Host ("signature: {0}" -f $signature.Status)
    if ($signature.SignerCertificate) {
        Write-Host ("signer:    {0}" -f $signature.SignerCertificate.Subject)
    }
    if ($RequireSignature -and $signature.Status -ne 'Valid') {
        throw "A valid Authenticode signature is required, but status is $($signature.Status)."
    }
}

if ($mismatched -gt 0 -or $missing -gt 0) {
    throw "Integrity verification failed."
}
