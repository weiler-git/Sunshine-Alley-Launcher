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

    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [ValidateLength(1, 32)]
    [string]$Channel = 'stable',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$MinimumVersion = '3.0.0',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$MinimumSupportedVersion,
    [string]$PublisherSubject = 'Sunshine Alley',
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CodeSigningCertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$DevelopmentUnsigned
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
$ReleaseRoot = Join-Path $ProjectRoot "artifacts/releases/$Version/win-x64"
$TransactionId = [Guid]::NewGuid().ToString('N')
$PublishRoot = Join-Path $ProjectRoot "artifacts/publish-update/$TransactionId/win-x64"
$ReleaseStaging = Join-Path $ProjectRoot "artifacts/release-staging/$TransactionId/win-x64"
$PublishedExecutable = Join-Path $PublishRoot 'SunshineAlleyLauncher.exe'
$ReleaseExecutable = Join-Path $ReleaseStaging 'SunshineAlleyLauncher.exe'

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
    throw "Release manifest private key not found. Run scripts/windows/Initialize-UpdateSigning.ps1 first."
}
if ((Get-Content $PublicKeyPath -Raw).StartsWith('UPDATE PUBLIC KEY NOT CONFIGURED')) {
    throw 'The embedded update public key has not been initialized.'
}
if (-not $DevelopmentUnsigned -and [string]::IsNullOrWhiteSpace($CodeSigningCertificateThumbprint)) {
    throw 'A production release requires -CodeSigningCertificateThumbprint. Use -DevelopmentUnsigned only for an isolated test deployment.'
}
if ([string]::IsNullOrWhiteSpace($PublisherSubject)) {
    throw 'PublisherSubject cannot be empty.'
}

if (-not $DevelopmentUnsigned) {
    $SigningCertificate = Get-Item "Cert:\CurrentUser\My\$CodeSigningCertificateThumbprint" -ErrorAction SilentlyContinue
    if ($null -eq $SigningCertificate) {
        throw "Code-signing certificate '$CodeSigningCertificateThumbprint' was not found in Cert:\CurrentUser\My."
    }
    if (-not $SigningCertificate.HasPrivateKey) {
        throw "Code-signing certificate '$CodeSigningCertificateThumbprint' is installed, but its private key is unavailable."
    }

    $SigningSimpleName = $SigningCertificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)

    if ($SigningSimpleName -ne $PublisherSubject -and
        $SigningCertificate.Subject -ne $PublisherSubject) {
        throw "Code-signing certificate publisher '$($SigningCertificate.Subject)' does not match PublisherSubject '$PublisherSubject'."
    }
}
if ($DevelopmentUnsigned -and $Channel -eq 'stable') {
    throw 'A development-unsigned launcher cannot be built for the stable channel.'
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

$AllowUnsignedUpdates = if ($DevelopmentUnsigned) { 'true' } else { 'false' }
$PublishArguments = @(
    'publish', $AppProject,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    "-p:Version=$Version",
    "-p:UpdatePublisherSubject=$PublisherSubject",
    "-p:UpdateSigningCertificateThumbprint=$CodeSigningCertificateThumbprint",
    "-p:AllowUnsignedUpdates=$AllowUnsignedUpdates",
    '-o', $PublishRoot
)
if ($DevelopmentUnsigned) {
    Write-Warning 'Building a DEVELOPMENT-ONLY launcher that accepts unsigned Authenticode updates.'
}

& dotnet @PublishArguments
if ($LASTEXITCODE -ne 0) { throw 'win-x64 launcher publish failed' }
if (-not (Test-Path $PublishedExecutable)) {
    throw "Published launcher not found at $PublishedExecutable"
}

if (-not $DevelopmentUnsigned) {
    $SignTool = (Get-Command signtool.exe -ErrorAction SilentlyContinue)?.Source

    if (-not $SignTool) {
        $SignTool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" `
            -Filter signtool.exe `
            -Recurse `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $SignTool) {
        throw "signtool.exe could not be found. Install the Windows SDK."
    }

    Write-Host "Using SignTool: $SignTool"



    & $SignTool sign /sha1 $CodeSigningCertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $PublishedExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed' }

    # A deliberately self-signed certificate is expected to report NotTrusted
    # on machines where it is not installed as a trusted root. NotTrusted is
    # accepted here; invalid/missing/hash-mismatched signatures are not.
    $Authenticode = Get-AuthenticodeSignature $PublishedExecutable
    if ($null -eq $Authenticode.SignerCertificate) {
        throw 'The signed executable does not contain an Authenticode signer certificate.'
    }

    $IsSelfSignedUntrustedRoot =
        $Authenticode.Status -eq 'UnknownError' -and
        $Authenticode.StatusMessage -like '*root certificate which is not trusted*'

    if ($Authenticode.Status -ne 'Valid' -and
        $Authenticode.Status -ne 'NotTrusted' -and
        -not $IsSelfSignedUntrustedRoot) {

        throw "Authenticode signature validation failed with status '$($Authenticode.Status)': $($Authenticode.StatusMessage)"
    }

    $ActualThumbprint = $Authenticode.SignerCertificate.Thumbprint.Replace(' ', '')
    if ($ActualThumbprint -ne $CodeSigningCertificateThumbprint) {
        throw "The signed executable certificate thumbprint '$ActualThumbprint' does not match expected thumbprint '$CodeSigningCertificateThumbprint'."
    }

    $SignerSimpleName = $Authenticode.SignerCertificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)

    if ($SignerSimpleName -ne $PublisherSubject -and
        $Authenticode.SignerCertificate.Subject -ne $PublisherSubject) {
        throw "The signed executable publisher does not exactly match PublisherSubject '$PublisherSubject'."
    }

    if ($null -eq $Authenticode.TimeStamperCertificate) {
        throw 'The signed executable does not contain a timestamp.'
    }

    Write-Host "Authenticode signer accepted: $($Authenticode.SignerCertificate.Subject)"
    Write-Host "Certificate thumbprint: $ActualThumbprint"

    if ($IsSelfSignedUntrustedRoot) {
        Write-Host "Windows reports the expected untrusted-root status for the self-signed certificate."
    }
}

Copy-Item $PublishedExecutable $ReleaseExecutable -Force
$PackageHash = (Get-FileHash $ReleaseExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
$PackageSize = (Get-Item $ReleaseExecutable).Length
$Base = $PackageBaseUrl.TrimEnd('/')
$PackageUrl = "$Base/$Version/win-x64/SunshineAlleyLauncher.exe"
$PublishedUtc = [DateTimeOffset]::UtcNow.ToString('o')

if (-not ('SunshineManifestSigner' -as [type])) {
    Add-Type -TypeDefinition @'
using System.IO;
using System.Security.Cryptography;

public static class SunshineManifestSigner
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
    $Signature = [SunshineManifestSigner]::Sign($PrivateKeyPath, $PayloadBytes)
    if (-not [SunshineManifestSigner]::Verify(
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
    $EnvelopeJson = $Envelope | ConvertTo-Json -Depth 4 -Compress
    [System.IO.File]::WriteAllText($Destination, $EnvelopeJson, [System.Text.UTF8Encoding]::new($false))
}

$UpdatePayload = [ordered]@{
    schemaVersion = 1
    updateAvailable = $true
    releaseId = $ReleaseId
    version = $Version
    runtimeIdentifier = 'win-x64'
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
    runtimeIdentifier = 'win-x64'
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
    runtimeIdentifier = 'win-x64'
    releaseId = $ReleaseId
    version = $Version
    minimumVersion = $MinimumVersion
    minimumSupportedVersion = $MinimumSupportedVersion
    executableFile = 'SunshineAlleyLauncher.exe'
    updateEnvelopeFile = 'update.envelope.json'
    noUpdateEnvelopeFile = 'no-update.envelope.json'
    packageUrl = $PackageUrl
    publishedUtc = $PublishedUtc
    publisherSubject = $PublisherSubject
    sha256 = $PackageHash
    size = $PackageSize
    developmentUnsigned = [bool]$DevelopmentUnsigned
}
[System.IO.File]::WriteAllText(
    (Join-Path $ReleaseStaging 'server-release.json'),
    ($ServerRecord | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force (Split-Path $ReleaseRoot -Parent) | Out-Null
[System.IO.Directory]::Move($ReleaseStaging, $ReleaseRoot)

Write-Host "Release prepared at $ReleaseRoot"
Write-Host "Package SHA-256: $PackageHash"
Write-Host 'Upload the four public release files and activate server-release.json according to API_DEPLOYMENT.md.'
