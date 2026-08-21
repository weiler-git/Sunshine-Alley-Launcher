using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace SunshineAlley.Core;

public sealed class RsaIdentityService
{
    private const int KeySize = 2048;
    private readonly ISecretStore _secretStore;
    private readonly ISettingsStore _settingsStore;

    public RsaIdentityService(ISecretStore secretStore, ISettingsStore settingsStore)
    {
        _secretStore = secretStore;
        _settingsStore = settingsStore;
    }

    public static string GetSecretName(string deviceId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId));
        return $"sunshine-alley.launcher.rsa.{Convert.ToHexString(digest[..16]).ToLowerInvariant()}";
    }

    public async Task<LauncherIdentity> LoadOrCreateAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!await _secretStore.IsAvailableAsync(cancellationToken))
        {
            throw new SecretStoreUnavailableException(
                "The operating-system secret service is unavailable. The launcher will not store an RSA private key in settings or in a plaintext file.");
        }

        string secretName = GetSecretName(deviceId);
        string? encodedPrivateKey = await _secretStore.GetAsync(secretName, cancellationToken);
        RSA rsa = RSA.Create(KeySize);
        try
        {
            if (string.IsNullOrWhiteSpace(encodedPrivateKey))
            {
                byte[] newPrivateKey = rsa.ExportPkcs8PrivateKey();
                try
                {
                    await _secretStore.SetAsync(
                        secretName,
                        Convert.ToBase64String(newPrivateKey),
                        cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(newPrivateKey);
                }
            }
            else
            {
                byte[]? privateKey = null;
                try
                {
                    privateKey = Convert.FromBase64String(encodedPrivateKey);
                    rsa.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
                    if (bytesRead != privateKey.Length)
                    {
                        throw new CryptographicException("The secret contains trailing RSA key data.");
                    }
                }
                catch (Exception exception) when (exception is FormatException or CryptographicException)
                {
                    throw new LauncherException(
                        "The RSA private key in the operating-system secret store is invalid. It was not replaced because doing so would change this device's server identity.",
                        exception);
                }
                finally
                {
                    if (privateKey is not null)
                    {
                        CryptographicOperations.ZeroMemory(privateKey);
                    }
                }
            }

            string publicKey = RsaXmlKey.ExportPublicKey(rsa);
            string signature = Convert.ToBase64String(
                rsa.SignData(
                    Encoding.UTF8.GetBytes(deviceId),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));

            await _settingsStore.SetAsync(
                SettingsKeys.RsaPublic(deviceId),
                publicKey,
                cancellationToken);
            await _settingsStore.SetAsync(
                SettingsKeys.RsaSignature(deviceId),
                signature,
                cancellationToken);

            return new LauncherIdentity(deviceId, publicKey, rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }
}

public sealed class LauncherIdentity : IDisposable
{
    private readonly RSA _privateKey;
    private bool _disposed;

    internal LauncherIdentity(string deviceId, string publicKeyXml, RSA privateKey)
    {
        DeviceId = deviceId;
        PublicKeyXml = publicKeyXml;
        _privateKey = privateKey;
    }

    public string DeviceId { get; }
    public string PublicKeyXml { get; }

    public string Sign(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] signature = _privateKey.SignData(
            Encoding.UTF8.GetBytes(message),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _privateKey.Dispose();
        _disposed = true;
    }
}

public static class RsaXmlKey
{
    public static string ExportPublicKey(RSA rsa)
    {
        RSAParameters parameters = rsa.ExportParameters(false);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"<RSAKeyValue><Modulus>{Convert.ToBase64String(parameters.Modulus!)}</Modulus><Exponent>{Convert.ToBase64String(parameters.Exponent!)}</Exponent></RSAKeyValue>");
    }

    public static RSA ImportPrivateKey(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        var document = new XmlDocument { XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        document.Load(reader);

        XmlElement root = document.DocumentElement
            ?? throw new CryptographicException("RSA XML has no document element.");
        if (!string.Equals(root.Name, "RSAKeyValue", StringComparison.Ordinal))
        {
            throw new CryptographicException("RSA XML has an invalid root element.");
        }

        byte[]? Read(string name)
        {
            XmlNode? node = root.SelectSingleNode(name);
            return string.IsNullOrWhiteSpace(node?.InnerText)
                ? null
                : Convert.FromBase64String(node.InnerText);
        }

        var parameters = new RSAParameters
        {
            Modulus = Read("Modulus"),
            Exponent = Read("Exponent"),
            P = Read("P"),
            Q = Read("Q"),
            DP = Read("DP"),
            DQ = Read("DQ"),
            InverseQ = Read("InverseQ"),
            D = Read("D")
        };

        RSA rsa = RSA.Create();
        try
        {
            rsa.ImportParameters(parameters);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public static string ConvertPrivateXmlToPkcs8(string xml)
    {
        using RSA rsa = ImportPrivateKey(xml);
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        try
        {
            return Convert.ToBase64String(privateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }
}
