using System.Security.Cryptography;
using System.Text.Json;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Update;

public sealed class UpdateManifestVerifier
{
    public const string TrustedKeyId = "sunshine-release-2026-01";
    private const string PublicKeyResource = "SunshineAlley.UpdatePublicKey.pem";
    private readonly string _publicKeyPem;

    public UpdateManifestVerifier(string? publicKeyPem = null) =>
        _publicKeyPem = publicKeyPem ?? LoadEmbeddedPublicKey();

    public VerifiedUpdateEnvelope Verify(SignedLauncherUpdateEnvelope envelope)
    {
        if (!string.Equals(envelope.KeyId, TrustedKeyId, StringComparison.Ordinal))
        {
            throw new LauncherException(
                $"The update manifest uses untrusted signing key '{envelope.KeyId}'.");
        }

        byte[] payload;
        byte[] signature;
        if (string.IsNullOrWhiteSpace(envelope.Payload)
            || string.IsNullOrWhiteSpace(envelope.Signature))
        {
            throw new LauncherException("The update envelope is incomplete.");
        }

        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException exception)
        {
            throw new LauncherException("The update envelope is not valid base64.", exception);
        }

        if (payload.Length == 0
            || payload.Length > 64 * 1024
            || signature.Length < 256
            || signature.Length > 1024)
        {
            throw new LauncherException("The update envelope exceeds its signed payload limits.");
        }

        using RSA rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(_publicKeyPem);
        }
        catch (Exception exception) when (exception is ArgumentException
            or CryptographicException)
        {
            throw new LauncherException(
                "The embedded update public key is not configured. Run the update-signing initialization script before publishing.",
                exception);
        }

        bool signatureValid;
        try
        {
            signatureValid = rsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException exception)
        {
            throw new LauncherException("The update manifest signature is invalid.", exception);
        }

        if (!signatureValid)
        {
            throw new LauncherException("The update manifest signature is invalid.");
        }

        LauncherUpdateManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(payload)
                ?? throw new JsonException("Manifest payload was JSON null.");
        }
        catch (JsonException exception)
        {
            throw new LauncherException("The signed update manifest contains invalid JSON.", exception);
        }

        if (manifest.SchemaVersion != 1)
        {
            throw new LauncherException(
                $"Unsupported update manifest schema {manifest.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.RuntimeIdentifier)
            || string.IsNullOrWhiteSpace(manifest.Channel)
            || manifest.ReleaseId <= 0
            || manifest.PublishedUtc == default
            || (!manifest.UpdateAvailable && manifest.Package is not null))
        {
            throw new LauncherException("The signed update manifest is incomplete or inconsistent.");
        }

        if (manifest.PublishedUtc > DateTimeOffset.UtcNow.AddHours(24))
        {
            throw new LauncherException("The update manifest publication time is in the future.");
        }

        return new VerifiedUpdateEnvelope(manifest, payload);
    }

    private static string LoadEmbeddedPublicKey()
    {
        using Stream stream = typeof(UpdateManifestVerifier).Assembly
            .GetManifestResourceStream(PublicKeyResource)
            ?? throw new LauncherException(
                $"Embedded update trust resource '{PublicKeyResource}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
