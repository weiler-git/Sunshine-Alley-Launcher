[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$SecretDirectory = Join-Path $ProjectRoot '.release-secrets'
$PrivateKey = Join-Path $SecretDirectory 'launcher-update-private.pem'
$PublicKey = Join-Path $ProjectRoot 'src/SunshineAlley.Platform/Update/update-public-key.pem'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required so the .NET PEM export APIs are available.'
}

if ((Test-Path $PrivateKey) -and -not $Force) {
    throw "A release-signing private key already exists at $PrivateKey. Use -Force only when intentionally rotating the release key."
}

New-Item -ItemType Directory -Force $SecretDirectory | Out-Null
$Rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $Rsa.KeySize = 3072
    [System.IO.File]::WriteAllText(
        $PrivateKey,
        $Rsa.ExportPkcs8PrivateKeyPem(),
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        $PublicKey,
        $Rsa.ExportSubjectPublicKeyInfoPem(),
        [System.Text.UTF8Encoding]::new($false))
}
finally {
    $Rsa.Dispose()
}

Write-Host "Private release key: $PrivateKey"
Write-Host "Embedded public key:  $PublicKey"
Write-Warning 'Back up the private key in a protected release secret store. Never commit or upload it with a release.'
Write-Warning 'Commit the public-key change before building the first production V3 release.'
