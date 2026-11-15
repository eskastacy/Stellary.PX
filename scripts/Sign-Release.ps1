#Requires -Version 5.1
<#
.SYNOPSIS
    Signs a Stellary.PX release with a real CA-issued certificate and verifies it.

.DESCRIPTION
    Requires signtool.exe and either a PFX file or a certificate thumbprint in the
    current user's certificate store. The script never creates a self-signed
    certificate and never reports success unless Get-AuthenticodeSignature returns
    Valid for every signed target.

.PARAMETER Path
    Executable, DLL, or directory to sign.

.PARAMETER Pfx
    Path to a PFX file containing the code-signing certificate.

.PARAMETER Password
    SecureString password for the PFX.

.PARAMETER Thumbprint
    SHA-1 thumbprint of a certificate already installed in the current user store.

.PARAMETER TimestampUrl
    RFC 3161 timestamp service. Defaults to DigiCert's public endpoint.

.PARAMETER Signtool
    Explicit path to signtool.exe when it is not on PATH or in a Windows Kit.
#>
[CmdletBinding(DefaultParameterSetName = 'Pfx')]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)]
    [string]$Pfx,

    [Parameter(ParameterSetName = 'Pfx')]
    [Security.SecureString]$Password,

    [Parameter(ParameterSetName = 'Thumbprint', Mandatory = $true)]
    [string]$Thumbprint,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [string]$Signtool
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    param([string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit -PathType Leaf)) {
            throw "signtool.exe not found at $Explicit"
        }
        return (Resolve-Path -LiteralPath $Explicit).Path
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($root in $roots) {
        $candidate = Get-ChildItem -LiteralPath $root -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    throw @'
signtool.exe was not found.

Install the Windows SDK signing tools, then rerun:
  winget install Microsoft.WindowsSDK

Or pass -Signtool <path>. Stellary.PX does not ship or simulate a signer.
'@
}

$signToolPath = Find-SignTool -Explicit $Signtool
$resolved = (Resolve-Path -LiteralPath $Path).Path

$targets = if (Test-Path -LiteralPath $resolved -PathType Container) {
    Get-ChildItem -LiteralPath $resolved -Recurse -File |
        Where-Object { $_.Extension -in '.exe', '.dll' } |
        Select-Object -ExpandProperty FullName
}
else {
    @($resolved)
}

if (-not $targets) {
    throw "No .exe or .dll files found under $resolved"
}

$common = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/v')

if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    if (-not (Test-Path -LiteralPath $Pfx -PathType Leaf)) {
        throw "PFX not found: $Pfx"
    }
    $pfxPath = (Resolve-Path -LiteralPath $Pfx).Path
    $common += @('/f', $pfxPath)
    if ($Password) {
        $bstr = [IntPtr]::Zero
        try {
            $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
            $common += @('/p', $plain)
            foreach ($target in $targets) {
                Write-Host "Signing $target"
                & $signToolPath @common $target
                if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
            }
        }
        finally {
            if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
            $plain = $null
        }
    }
    else {
        foreach ($target in $targets) {
            Write-Host "Signing $target"
            & $signToolPath @common $target
            if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
        }
    }
}
else {
    $common += @('/sha1', $Thumbprint)
    foreach ($target in $targets) {
        Write-Host "Signing $target"
        & $signToolPath @common $target
        if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
    }
}

foreach ($target in $targets) {
    $signature = Get-AuthenticodeSignature -LiteralPath $target
    Write-Host ("{0}: {1}" -f (Split-Path -Leaf $target), $signature.Status)
    if ($signature.Status -ne 'Valid') {
        throw "Signature verification failed for $target ($($signature.Status)): $($signature.StatusMessage)"
    }
    if ($signature.SignerCertificate) {
        Write-Host ("  signer: {0}" -f $signature.SignerCertificate.Subject)
    }
}
