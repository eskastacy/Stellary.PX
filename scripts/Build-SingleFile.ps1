#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, tests, signs, and hashes the single-file Stellary.PX.exe.

.DESCRIPTION
    Produces:
      dist\Stellary.PX-<version>-<runtime>.exe
      dist\Stellary.PX-<version>-<runtime>.exe.sha256

    The uncap payload, branding, and profile avatar are embedded in the
    assembly, so the executable runs on its own and is the only release asset.
    Signing is opt-in and requires a real CA-issued certificate.

.EXAMPLE
    .\scripts\Build-SingleFile.ps1 -Version 0.1.3

.EXAMPLE
    .\scripts\Build-SingleFile.ps1 -Version 0.1.3 -Sign -Pfx .\secrets\codesign.pfx `
        -PfxPassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Configuration = 'Release',

    [string]$OutputRoot,

    [switch]$SkipTests,

    [switch]$Sign,

    [string]$Pfx,

    [Security.SecureString]$PfxPassword,

    [string]$Thumbprint,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Stellary.PX\Stellary.PX.csproj'
$solution = Join-Path $repoRoot 'Stellary.PX.sln'
$tests = Join-Path $repoRoot 'tests\Stellary.PX.Tests\Stellary.PX.Tests.csproj'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

function Resolve-Dotnet {
    # Prefer a local SDK install when the PATH host has only a runtime.
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'dotnet-sdk\dotnet.exe'),
        (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    )
    foreach ($candidate in $candidates) {
        if (-not $candidate -or -not (Test-Path -LiteralPath $candidate)) { continue }
        try {
            $sdks = & $candidate --list-sdks 2>$null
            if ($LASTEXITCODE -eq 0 -and $sdks) { return $candidate }
        }
        catch { }
    }
    throw 'dotnet with an installed SDK was not found. Install the .NET 8 SDK and rerun.'
}

$dotnet = Resolve-Dotnet

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $Version = ($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Could not determine a version. Pass -Version x.y.z.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$') {
    throw "Invalid version '$Version'. Use a semantic version such as 0.1.3."
}

$numericVersion = $Version.Split('-')[0]
$versionParts = $numericVersion.Split('.')
$fileVersion = switch ($versionParts.Count) {
    3 { "$numericVersion.0" }
    4 { $numericVersion }
    default { throw "Version '$Version' must have three or four numeric parts." }
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'dist'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$exe = Join-Path $OutputRoot "Stellary.PX-$Version-$Runtime.exe"
$hashFile = "$exe.sha256"
$stage = Join-Path $OutputRoot ".single-$Version-$Runtime"

$shouldSign = $Sign -or $Pfx -or $Thumbprint
if ($shouldSign -and -not $Pfx -and -not $Thumbprint) {
    throw 'Signing requested but no -Pfx or -Thumbprint was supplied.'
}
if ($RequireSignature -and -not $shouldSign) {
    throw '-RequireSignature needs -Sign with -Pfx or -Thumbprint.'
}

Write-Host ''
Write-Host 'Stellary.PX single-file release'
Write-Host "  version       $Version"
Write-Host "  runtime       $Runtime"
Write-Host "  configuration $Configuration"
Write-Host "  signing       $(if ($shouldSign) { 'requested' } else { 'not requested (unsigned build)' })"
Write-Host ''

Write-Host '== Build =='
& $dotnet build $solution -c $Configuration -v minimal --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

if (-not $SkipTests) {
    Write-Host ''
    Write-Host '== Service tests (read-only) =='
    & $dotnet run --project $tests -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Service tests failed with exit code $LASTEXITCODE" }
}

Write-Host ''
Write-Host '== Publish single file =='
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

& $dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $stage `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    ('-p:Version=' + $Version) `
    ('-p:AssemblyVersion=' + $fileVersion) `
    ('-p:FileVersion=' + $fileVersion) `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

$published = Join-Path $stage 'Stellary.PX.exe'
if (-not (Test-Path -LiteralPath $published)) {
    throw "Publish did not produce $published"
}

if ($shouldSign) {
    Write-Host ''
    Write-Host '== Sign =='
    $signArgs = @{ Path = $published }
    if ($Pfx) {
        $signArgs.Pfx = $Pfx
        if ($PfxPassword) { $signArgs.Password = $PfxPassword }
    }
    if ($Thumbprint) { $signArgs.Thumbprint = $Thumbprint }
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
}

$signature = Get-AuthenticodeSignature -LiteralPath $published
Write-Host ("Authenticode: {0}" -f $signature.Status)
if ($signature.SignerCertificate) {
    Write-Host ("Signer:       {0}" -f $signature.SignerCertificate.Subject)
}
if ($RequireSignature -and $signature.Status -ne 'Valid') {
    throw "A valid signature is required, but status is $($signature.Status)."
}
if (-not $shouldSign) {
    Write-Warning 'This build is UNSIGNED. Publish it as unsigned; do not claim a signature.'
}

Write-Host ''
Write-Host '== Package =='
Copy-Item -LiteralPath $published -Destination $exe -Force
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$ascii = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($hashFile, "$hash  $(Split-Path -Leaf $exe)`n", $ascii)
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Host ''
Write-Host 'Release artifact'
Write-Host "  exe     $exe"
Write-Host "  sums    $hashFile"
Write-Host ("  sha256  {0}" -f $hash)
Write-Host ''
