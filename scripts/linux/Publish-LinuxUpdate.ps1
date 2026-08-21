[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$ReleaseId,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$PackageBaseUrl,

    [ValidateSet('linux-x64')]
    [string]$RuntimeIdentifier = 'linux-x64',
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [ValidateLength(1, 32)]
    [string]$Channel = 'stable',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$MinimumVersion = '3.0.0',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$MinimumSupportedVersion
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($MinimumSupportedVersion)) {
    $MinimumSupportedVersion = $null
}
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Solution = Join-Path $ProjectRoot 'Sunshine Alley Launcher.sln'
$AppProject = Join-Path $ProjectRoot 'src/SunshineAlley.App/SunshineAlley.App.csproj'
$SmokeProject = Join-Path $ProjectRoot 'tests/SunshineAlley.SmokeTests/SunshineAlley.SmokeTests.csproj'
$PrivateKeyPath = Join-Path $ProjectRoot '.release-secrets/launcher-update-private.pem'
$PublicKeyPath = Join-Path $ProjectRoot 'src/SunshineAlley.Platform/Update/update-public-key.pem'
$ReleaseRoot = Join-Path $ProjectRoot "artifacts/releases/$Version/$RuntimeIdentifier"
$TransactionId = [Guid]::NewGuid().ToString('N')
$PublishRoot = Join-Path $ProjectRoot "artifacts/publish-update/$TransactionId/$RuntimeIdentifier"
$ReleaseStaging = Join-Path $ProjectRoot "artifacts/release-staging/$TransactionId/$RuntimeIdentifier"
$PublishedExecutable = Join-Path $PublishRoot 'SunshineAlleyLauncher'
$ReleaseExecutable = Join-Path $ReleaseStaging 'SunshineAlleyLauncher'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required.'
}
$ReleaseVersion = [Version]$Version
if ([Version]$MinimumVersion -gt $ReleaseVersion) {
    throw 'MinimumVersion cannot be newer than the release Version.'
}
if (-not [string]::IsNullOrWhiteSpace($MinimumSupportedVersion) -and
    [Version]$MinimumSupportedVersion -gt $ReleaseVersion) {
    throw 'MinimumSupportedVersion cannot be newer than the release Version.'
}
if (-not (Test-Path $PrivateKeyPath)) {
    throw 'Release manifest private key not found. Run scripts/windows/Initialize-UpdateSigning.ps1 once and protect the generated private key.'
}
if ((Get-Content $PublicKeyPath -Raw).StartsWith('UPDATE PUBLIC KEY NOT CONFIGURED')) {
    throw 'The embedded update public key has not been initialized.'
}
if (Test-Path $ReleaseRoot) {
    throw "The immutable release directory already exists: $ReleaseRoot. Use a new version."
}

$PackageUri = [Uri]$PackageBaseUrl
if ($PackageUri.Scheme -ne 'https' -or
    $PackageUri.Host -ne 'sunshinealley.games' -or
    $PackageUri.Port -ne 443 -or
    -not [string]::IsNullOrEmpty($PackageUri.UserInfo) -or
    -not [string]::IsNullOrEmpty($PackageUri.Query) -or
    -not [string]::IsNullOrEmpty($PackageUri.Fragment)) {
    throw 'PackageBaseUrl must be an HTTPS URL on sunshinealley.games using port 443, with no user information, query, or fragment.'
}

New-Item -ItemType Directory -Force $PublishRoot, $ReleaseStaging | Out-Null

dotnet restore $Solution
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
dotnet run --project $SmokeProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'smoke tests failed' }

dotnet publish $AppProject `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:Version=$Version `
    -o $PublishRoot
if ($LASTEXITCODE -ne 0) { throw "$RuntimeIdentifier launcher publish failed" }
if (-not (Test-Path $PublishedExecutable)) {
    throw "Published Linux launcher not found at $PublishedExecutable"
}

if (-not $IsWindows) {
    [System.IO.File]::SetUnixFileMode(
        $PublishedExecutable,
        [System.IO.UnixFileMode]::UserRead -bor
        [System.IO.UnixFileMode]::UserWrite -bor
        [System.IO.UnixFileMode]::UserExecute)
}

Copy-Item $PublishedExecutable $ReleaseExecutable -Force
$PackageHash = (Get-FileHash $ReleaseExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
$PackageSize = (Get-Item $ReleaseExecutable).Length
$Base = $PackageBaseUrl.TrimEnd('/')
$PackageUrl = "$Base/$Version/$RuntimeIdentifier/SunshineAlleyLauncher"
$PublishedUtc = [DateTimeOffset]::UtcNow.ToString('o')

if (-not ('SunshineLinuxManifestSigner' -as [type])) {
    Add-Type -TypeDefinition @'
using System.IO;
using System.Security.Cryptography;

public static class SunshineLinuxManifestSigner
{
    public static byte[] Sign(string privateKeyPath, byte[] payload)
    {
        using (RSA rsa = RSA.Create())
        {
            rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
            return rsa.SignData(
                payload,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
    }

    public static bool Verify(string publicKeyPath, byte[] payload, byte[] signature)
    {
        using (RSA rsa = RSA.Create())
        {
            rsa.ImportFromPem(File.ReadAllText(publicKeyPath));
            return rsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
    }
}
'@
}

function New-SignedEnvelope {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary]$Payload,
        [Parameter(Mandatory)] [string]$Destination
    )

    $PayloadJson = $Payload | ConvertTo-Json -Depth 10 -Compress
    $PayloadBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($PayloadJson)
    $Signature = [SunshineLinuxManifestSigner]::Sign($PrivateKeyPath, $PayloadBytes)
    if (-not [SunshineLinuxManifestSigner]::Verify(
        $PublicKeyPath,
        $PayloadBytes,
        $Signature)) {
        throw 'The offline private key does not match the public key embedded in the launcher.'
    }
    $Envelope = [ordered]@{
        payload = [Convert]::ToBase64String($PayloadBytes)
        signature = [Convert]::ToBase64String($Signature)
        keyId = 'sunshine-release-2026-01'
    }
    [System.IO.File]::WriteAllText(
        $Destination,
        ($Envelope | ConvertTo-Json -Depth 4 -Compress),
        [System.Text.UTF8Encoding]::new($false))
}

$UpdatePayload = [ordered]@{
    schemaVersion = 1
    updateAvailable = $true
    releaseId = $ReleaseId
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    channel = $Channel
    minimumVersion = $MinimumVersion
    minimumSupportedVersion = $MinimumSupportedVersion
    publishedUtc = $PublishedUtc
    package = [ordered]@{
        url = $PackageUrl
        size = $PackageSize
        sha256 = $PackageHash
    }
}
$NoUpdatePayload = [ordered]@{
    schemaVersion = 1
    updateAvailable = $false
    releaseId = $ReleaseId
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    channel = $Channel
    minimumVersion = $MinimumVersion
    minimumSupportedVersion = $MinimumSupportedVersion
    publishedUtc = $PublishedUtc
    package = $null
}

New-SignedEnvelope $UpdatePayload (Join-Path $ReleaseStaging 'update.envelope.json')
New-SignedEnvelope $NoUpdatePayload (Join-Path $ReleaseStaging 'no-update.envelope.json')

$ServerRecord = [ordered]@{
    channel = $Channel
    runtimeIdentifier = $RuntimeIdentifier
    releaseId = $ReleaseId
    version = $Version
    minimumVersion = $MinimumVersion
    minimumSupportedVersion = $MinimumSupportedVersion
    executableFile = 'SunshineAlleyLauncher'
    updateEnvelopeFile = 'update.envelope.json'
    noUpdateEnvelopeFile = 'no-update.envelope.json'
    packageUrl = $PackageUrl
    publishedUtc = $PublishedUtc
    sha256 = $PackageHash
    size = $PackageSize
}
[System.IO.File]::WriteAllText(
    (Join-Path $ReleaseStaging 'server-release.json'),
    ($ServerRecord | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force (Split-Path $ReleaseRoot -Parent) | Out-Null
[System.IO.Directory]::Move($ReleaseStaging, $ReleaseRoot)

Write-Host "Linux release prepared at $ReleaseRoot"
Write-Host "Package SHA-256: $PackageHash"
Write-Host 'Upload the raw executable and three JSON files, then activate the matching RID/channel record.'
