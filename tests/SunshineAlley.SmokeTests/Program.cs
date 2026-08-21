using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SunshineAlley.Core;
using SunshineAlley.Platform;
using SunshineAlley.Platform.Identity;
using SunshineAlley.Platform.Storage;
using SunshineAlley.Platform.Update;

namespace SunshineAlley.SmokeTests;

internal static class Program
{
    public static async Task<int> Main()
    {
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "sunshine-alley-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            StableDeviceGuid();
            PathContainment(temporaryRoot);
            await UnsafeModDataRootIsRejectedAsync(temporaryRoot);
            SymlinkIsRejected(temporaryRoot);
            await SettingsRoundTripAsync(temporaryRoot);
            await HashingAsync(temporaryRoot);
            RsaXmlCompatibility();
            SignedUpdateManifest();
            WindowsDefaultLayout();
            Console.WriteLine("All smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    private static void StableDeviceGuid()
    {
        string first = DeviceIdentityService.CreateStableGuid("machine-a");
        string second = DeviceIdentityService.CreateStableGuid("machine-a");
        string other = DeviceIdentityService.CreateStableGuid("machine-b");
        Assert(first == second, "Device GUID must be deterministic.");
        Assert(first != other, "Different device sources must produce different GUIDs.");
        Assert(Guid.TryParse(first, out _), "Device identity must be a GUID.");
    }

    private static void PathContainment(string root)
    {
        string child = Path.Combine(root, "ModPacks", "1", "file.dll");
        string sibling = Path.Combine(Path.GetDirectoryName(root)!, "outside.dll");
        Assert(PathSecurity.IsUnderRoot(root, child), "Child path must be accepted.");
        Assert(!PathSecurity.IsUnderRoot(root, sibling), "Sibling path must be rejected.");
    }

    private static async Task UnsafeModDataRootIsRejectedAsync(string root)
    {
        var policy = new ModDataDirectoryPolicy();
        DirectoryValidationResult result = await policy.ValidateAsync(
            root,
            string.Empty,
            false);
        Assert(!result.IsValid, "The temporary-files directory must not be accepted for mod data.");
    }

    private static void SymlinkIsRejected(string root)
    {
        string target = Path.Combine(root, "link-target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Assert(
                PathSecurity.ContainsReparsePoint(Path.Combine(link, "file.dll")),
                "A path through a symbolic link must be rejected.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or PlatformNotSupportedException
            or IOException)
        {
            // Some Windows test hosts do not permit creating symbolic links.
        }
    }

    private static async Task SettingsRoundTripAsync(string root)
    {
        string path = Path.Combine(root, "settings.json");
        var store = new JsonSettingsStore(path);
        await store.SetAsync("Text", "value");
        await store.SetAsync("Flag", true);
        Assert(await store.GetAsync<string>("Text") == "value", "String setting round-trip failed.");
        Assert(await store.GetAsync<bool?>("Flag") == true, "Boolean setting round-trip failed.");
    }

    private static async Task HashingAsync(string root)
    {
        string file = Path.Combine(root, "hash.txt");
        await File.WriteAllTextAsync(file, "abc");
        string hash = await ModPackService.ComputeFileHashAsync(file);
        Assert(
            hash == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "SHA-256 hash is incorrect.");
    }

    private static void RsaXmlCompatibility()
    {
        using RSA source = RSA.Create(2048);
        RSAParameters parameters = source.ExportParameters(true);
        string xml = "<RSAKeyValue>"
            + Element("Modulus", parameters.Modulus)
            + Element("Exponent", parameters.Exponent)
            + Element("P", parameters.P)
            + Element("Q", parameters.Q)
            + Element("DP", parameters.DP)
            + Element("DQ", parameters.DQ)
            + Element("InverseQ", parameters.InverseQ)
            + Element("D", parameters.D)
            + "</RSAKeyValue>";
        using RSA imported = RsaXmlKey.ImportPrivateKey(xml);
        byte[] data = Encoding.UTF8.GetBytes("identity");
        byte[] signature = imported.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert(
            source.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "Legacy RSA XML migration is not compatible.");
    }

    private static void SignedUpdateManifest()
    {
        using RSA signingKey = RSA.Create(2048);
        var manifest = new LauncherUpdateManifest
        {
            SchemaVersion = 1,
            UpdateAvailable = true,
            ReleaseId = 7,
            Version = "3.1.0",
            RuntimeIdentifier = "win-x64",
            Channel = "stable",
            MinimumVersion = "3.0.0",
            PublishedUtc = DateTimeOffset.UtcNow,
            Package = new LauncherUpdatePackage
            {
                Url = "https://sunshinealley.games/launcher/releases/3.1.0/win-x64/SunshineAlleyLauncher.exe",
                Size = 123,
                Sha256 = new string('a', 64)
            }
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest);
        byte[] signature = signingKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var envelope = new SignedLauncherUpdateEnvelope
        {
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(signature),
            KeyId = UpdateManifestVerifier.TrustedKeyId
        };
        var verifier = new UpdateManifestVerifier(
            signingKey.ExportSubjectPublicKeyInfoPem());
        VerifiedUpdateEnvelope verified = verifier.Verify(envelope);
        Assert(verified.Manifest.ReleaseId == 7, "Signed update manifest verification failed.");

        payload[0] ^= 1;
        var tampered = new SignedLauncherUpdateEnvelope
        {
            Payload = Convert.ToBase64String(payload),
            Signature = envelope.Signature,
            KeyId = envelope.KeyId
        };
        bool rejected = false;
        try
        {
            verifier.Verify(tampered);
        }
        catch (LauncherException)
        {
            rejected = true;
        }

        Assert(rejected, "A tampered update manifest must be rejected.");
    }

    private static void WindowsDefaultLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        PlatformPaths paths = PlatformPaths.CreateDefault();
        Assert(
            Path.GetFileName(paths.ApplicationDirectory) == "App",
            "Windows application directory must default to App.");
        Assert(
            Path.GetFileName(paths.ConfigurationDirectory) == "Config",
            "Windows configuration directory must default to Config.");
        Assert(
            Path.GetFileName(paths.DataDirectory) == "Data",
            "Windows mod-data directory must default to Data.");
        Assert(
            !PathSecurity.IsUnderRoot(paths.ApplicationDirectory, paths.DataDirectory)
            && !PathSecurity.IsUnderRoot(paths.DataDirectory, paths.ApplicationDirectory),
            "Windows App and Data defaults must be separate sibling directories.");
    }

    private static string Element(string name, byte[]? value) =>
        $"<{name}>{Convert.ToBase64String(value!)}</{name}>";

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
