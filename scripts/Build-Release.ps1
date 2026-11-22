#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, tests, publishes, optionally signs, hashes, and archives Stellary.PX.

.DESCRIPTION
    Produces:
      dist\Stellary.PX-<version>-win-x64\        published application + docs
      dist\Stellary.PX-<version>-win-x64\SHA256SUMS.txt
      dist\Stellary.PX-<version>-win-x64.zip
      dist\Stellary.PX-<version>-win-x64.zip.sha256

    Signing is opt-in and requires a real CA-issued certificate. Without one the
    build is explicitly reported as unsigned and is never labeled otherwise.

.EXAMPLE
    .\scripts\Build-Release.ps1 -Version 0.1.2 -SkipTests

.EXAMPLE
    .\scripts\Build-Release.ps1 -Version 0.1.2 -Sign -Pfx .\secrets\codesign.pfx `
        -PfxPassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Configuration = 'Release',

    [string]$OutputRoot,

    [switch]$SelfContained,

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
    throw "Invalid version '$Version'. Use a semantic version such as 0.1.0."
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

$stageName = "Stellary.PX-$Version-$Runtime"
$stage = Join-Path $OutputRoot $stageName
$zip = "$stage.zip"
$zipSums = "$zip.sha256"

$shouldSign = $Sign -or $Pfx -or $Thumbprint
if ($shouldSign -and -not $Pfx -and -not $Thumbprint) {
    throw 'Signing requested but no -Pfx or -Thumbprint was supplied.'
}
if ($RequireSignature -and -not $shouldSign) {
    throw '-RequireSignature needs -Sign with -Pfx or -Thumbprint.'
}

Write-Host ''
Write-Host 'Stellary.PX release build'
Write-Host "  version       $Version"
Write-Host "  runtime       $Runtime"
Write-Host "  configuration $Configuration"
Write-Host "  self-contained $($SelfContained.IsPresent)"
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
Write-Host '== Publish =='
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $selfContainedValue,
    '-o', $stage,
    ('-p:Version=' + $Version),
    ('-p:AssemblyVersion=' + $fileVersion),
    ('-p:FileVersion=' + $fileVersion),
    '--nologo'
)
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host '== Assemble release folder =='

$publishExe = Join-Path $stage 'Stellary.PX.exe'
if (-not (Test-Path -LiteralPath $publishExe)) {
    throw "Publish did not produce $publishExe"
}

$topLevelDocs = @('README.md', 'LICENSE', 'CHANGELOG.md', 'SECURITY.md', 'CONTRIBUTING.md')
foreach ($name in $topLevelDocs) {
    $source = Join-Path $repoRoot $name
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name) -Force
    }
}

$docsSource = Join-Path $repoRoot 'docs'
if (Test-Path -LiteralPath $docsSource) {
    $docsTarget = Join-Path $stage 'docs'
    New-Item -ItemType Directory -Path $docsTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $docsSource '*.md') -Destination $docsTarget -Force
}

$screensSource = Join-Path $repoRoot 'screenshots'
if (Test-Path -LiteralPath $screensSource) {
    $screensTarget = Join-Path $stage 'screenshots'
    New-Item -ItemType Directory -Path $screensTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $screensSource '*.png') -Destination $screensTarget -Force
}

$scriptsTarget = Join-Path $stage 'scripts'
New-Item -ItemType Directory -Path $scriptsTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Destination $scriptsTarget -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'New-Checksums.ps1') -Destination $scriptsTarget -Force

if ($shouldSign) {
    Write-Host ''
    Write-Host '== Sign =='
    $signArgs = @{ Path = $publishExe }
    if ($Pfx) {
        $signArgs.Pfx = $Pfx
        if ($PfxPassword) { $signArgs.Password = $PfxPassword }
    }
    if ($Thumbprint) { $signArgs.Thumbprint = $Thumbprint }
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
}

Write-Host ''
Write-Host '== Checksums =='
& (Join-Path $PSScriptRoot 'New-Checksums.ps1') -Path $stage
if ($LASTEXITCODE -ne 0) { throw "Checksum generation failed with exit code $LASTEXITCODE" }

$signature = Get-AuthenticodeSignature -LiteralPath $publishExe
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
Write-Host '== Archive =='
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
if (Test-Path -LiteralPath $zipSums) { Remove-Item -LiteralPath $zipSums -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$ascii = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($zipSums, "$zipHash  $(Split-Path -Leaf $zip)`n", $ascii)

Write-Host ''
Write-Host 'Release artifacts'
Write-Host "  folder  $stage"
Write-Host "  zip     $zip"
Write-Host "  sums    $(Join-Path $stage 'SHA256SUMS.txt')"
Write-Host "  zip sum $zipSums"
Write-Host ("  sha256  {0}" -f $zipHash)
Write-Host ''
