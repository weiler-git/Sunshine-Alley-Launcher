using System.Security.Cryptography;
using System.Text;
using SunshineAlley.Core;
using SunshineAlley.Platform.Identity;
using SunshineAlley.Platform.Storage;

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
            await SettingsRoundTripAsync(temporaryRoot);
            await HashingAsync(temporaryRoot);
            RsaXmlCompatibility();
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
