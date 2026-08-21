using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SunshineAlley.Core;
using SunshineAlley.Platform;
using SunshineAlley.Platform.Identity;
using SunshineAlley.Platform.Installation;
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
            await LauncherPathPreferencesRoundTripAsync(temporaryRoot);
            await HashingAsync(temporaryRoot);
            RsaXmlCompatibility();
            SignedUpdateManifest();
            LinuxXdgLayouts(temporaryRoot);
            LinuxDesktopEntry();
            MandatoryUpdatePolicy();
            LinuxInstallDetection(temporaryRoot);
            LinuxUpdateReplacement(temporaryRoot);
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

    private static async Task LauncherPathPreferencesRoundTripAsync(string root)
    {
        string settingsFile = Path.Combine(root, "preferences", "settings.json");
        var settings = new LauncherSettings(
            new JsonSettingsStore(settingsFile),
            Path.Combine(root, "default data"));
        var expected = new LauncherPreferences(
            Path.Combine(root, "managed mods with spaces"),
            Path.Combine(root, "Valheim native with spaces"),
            true,
            false,
            "Example World");
        await settings.SaveAsync(expected);
        LauncherPreferences actual = await settings.LoadAsync();
        Assert(
            actual.ModDataDirectory == expected.ModDataDirectory,
            "The managed-mod path was not persisted.");
        Assert(
            actual.GameDirectory == expected.GameDirectory,
            "The native game path was not persisted.");
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

    private static void LinuxXdgLayouts(string root)
    {
        string home = Path.Combine(root, "home");
        PlatformPaths defaults = PlatformPaths.CreateLinux(home, _ => null);
        Assert(
            defaults.ApplicationDirectory == Path.Combine(
                home,
                ".local",
                "share",
                "sunshine-alley",
                "launcher"),
            "Linux application path must use the XDG data fallback.");
        Assert(
            defaults.SettingsFile == Path.Combine(
                home,
                ".config",
                "sunshine-alley",
                "launcher",
                "settings.json"),
            "Linux settings path must use the XDG config fallback.");
        Assert(
            defaults.UpdateCacheDirectory == Path.Combine(
                home,
                ".cache",
                "sunshine-alley",
                "launcher",
                "Updates"),
            "Linux updates must use the XDG cache fallback.");
        Assert(
            defaults.StateDirectory == Path.Combine(
                home,
                ".local",
                "state",
                "sunshine-alley",
                "launcher"),
            "Linux state must use the XDG state fallback.");
        Assert(
            defaults.LinuxDesktopEntryPath == Path.Combine(
                home,
                ".local",
                "share",
                "applications",
                PlatformPaths.LinuxApplicationId + ".desktop"),
            "Linux desktop integration must use XDG_DATA_HOME/applications.");

        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(root, "custom config"),
            ["XDG_DATA_HOME"] = Path.Combine(root, "custom data"),
            ["XDG_CACHE_HOME"] = Path.Combine(root, "custom cache"),
            ["XDG_STATE_HOME"] = Path.Combine(root, "custom state")
        };
        PlatformPaths custom = PlatformPaths.CreateLinux(
            home,
            name => variables.GetValueOrDefault(name));
        Assert(
            custom.ApplicationDirectory == Path.Combine(
                variables["XDG_DATA_HOME"]!,
                "sunshine-alley",
                "launcher"),
            "Customized XDG_DATA_HOME was not honored.");
        Assert(
            custom.ConfigurationDirectory.StartsWith(
                variables["XDG_CONFIG_HOME"]!,
                StringComparison.Ordinal),
            "Customized XDG_CONFIG_HOME was not honored.");
        Assert(
            custom.CacheDirectory.StartsWith(
                variables["XDG_CACHE_HOME"]!,
                StringComparison.Ordinal),
            "Customized XDG_CACHE_HOME was not honored.");
        Assert(
            custom.StateDirectory.StartsWith(
                variables["XDG_STATE_HOME"]!,
                StringComparison.Ordinal),
            "Customized XDG_STATE_HOME was not honored.");

        variables["XDG_DATA_HOME"] = "relative-data";
        PlatformPaths invalid = PlatformPaths.CreateLinux(
            home,
            name => variables.GetValueOrDefault(name));
        Assert(
            invalid.ApplicationDirectory.StartsWith(
                Path.Combine(home, ".local", "share"),
                StringComparison.Ordinal),
            "A relative XDG_DATA_HOME must be ignored.");

        variables["XDG_DATA_HOME"] = "invalid\0data";
        PlatformPaths malformed = PlatformPaths.CreateLinux(
            home,
            name => variables.GetValueOrDefault(name));
        Assert(
            malformed.ApplicationDirectory.StartsWith(
                Path.Combine(home, ".local", "share"),
                StringComparison.Ordinal),
            "A malformed XDG_DATA_HOME must be ignored.");
    }

    private static void LinuxDesktopEntry()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string entry = LinuxDesktopIntegration.CreateDesktopEntry(
            "/home/player/Applications with spaces 100%/SunshineAlleyLauncher",
            "/home/player/icons with spaces/sunshine.png");
        Assert(
            entry.Contains(
                "Exec=\"/home/player/Applications with spaces 100%%/SunshineAlleyLauncher\"",
                StringComparison.Ordinal),
            "The desktop Exec path must be safely quoted and escape field-code markers.");
        Assert(
            entry.Contains("Terminal=false", StringComparison.Ordinal)
            && entry.Contains("Categories=Game;", StringComparison.Ordinal),
            "The Linux desktop entry is incomplete.");
    }

    private static void MandatoryUpdatePolicy()
    {
        var manifest = new LauncherUpdateManifest
        {
            SchemaVersion = 1,
            UpdateAvailable = true,
            ReleaseId = 8,
            Version = "3.2.0",
            RuntimeIdentifier = "linux-x64",
            Channel = "stable",
            MinimumVersion = "3.0.0",
            MinimumSupportedVersion = "3.1.0",
            PublishedUtc = DateTimeOffset.UtcNow,
            Package = new LauncherUpdatePackage
            {
                Url = "https://sunshinealley.games/launcher/releases/3.2.0/linux-x64/SunshineAlleyLauncher",
                Size = 123,
                Sha256 = new string('b', 64)
            }
        };
        Assert(
            LauncherUpdatePolicy.IsCurrentVersionUnsupported(manifest, "3.0.9"),
            "A version below minimumSupportedVersion must be blocked.");
        Assert(
            !LauncherUpdatePolicy.IsCurrentVersionUnsupported(manifest, "3.1.0"),
            "The minimum supported version itself must remain supported.");
        string? remembered = LauncherUpdatePolicy.SelectHigherMinimumSupportedVersion(
            "3.2.0",
            "3.1.0");
        Assert(
            remembered == "3.2.0"
            && LauncherUpdatePolicy.IsCurrentVersionUnsupported(
                remembered,
                "3.1.9"),
            "A remembered signed minimum-supported floor must not be lowered by replay.");
    }

    private static void LinuxInstallDetection(string root)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string testRoot = Path.Combine(root, "linux-install-detection");
        PlatformPaths paths = CreateTestLinuxPaths(testRoot);
        string downloaded = Path.Combine(testRoot, "Downloads", "SunshineAlleyLauncher");
        Directory.CreateDirectory(Path.GetDirectoryName(downloaded)!);
        File.WriteAllText(downloaded, "downloaded");
        LinuxInstallationService.SetExecutableMode(downloaded);

        var downloadedService = new LinuxInstallationService(paths, () => downloaded);
        Assert(
            !downloadedService.Inspect().IsInstalled,
            "A Linux installation must not exist before its executable and marker.");

        Directory.CreateDirectory(paths.ApplicationDirectory);
        File.WriteAllText(paths.LinuxExecutablePath, "installed");
        LinuxInstallationService.SetExecutableMode(paths.LinuxExecutablePath);
        File.WriteAllText(
            paths.InstallationMarkerFile,
            JsonSerializer.Serialize(new InstallationMarker(
                LauncherInstallationConstants.ProductId,
                LauncherInstallationConstants.LayoutVersion,
                "3.0.0",
                "linux-x64")));
        InstallationInspection external = downloadedService.Inspect();
        Assert(
            external.IsInstalled && !external.IsCurrentExecutable,
            "A downloaded Linux copy must detect the existing fixed installation.");

        var installedService = new LinuxInstallationService(
            paths,
            () => paths.LinuxExecutablePath);
        Assert(
            installedService.Inspect().IsCurrentExecutable,
            "The fixed Linux executable must detect itself as installed.");
    }

    private static void LinuxUpdateReplacement(string root)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string testRoot = Path.Combine(root, "linux update with spaces");
        PlatformPaths paths = CreateTestLinuxPaths(testRoot);
        string transactionId = Guid.NewGuid().ToString("N");
        string transaction = Path.Combine(paths.UpdateCacheDirectory, transactionId);
        Directory.CreateDirectory(paths.ApplicationDirectory);
        Directory.CreateDirectory(transaction);
        string installed = paths.LinuxExecutablePath;
        string staged = Path.Combine(
            transaction,
            LauncherInstallationConstants.LinuxExecutableFileName + ".new");
        File.WriteAllText(installed, "old launcher");
        File.WriteAllText(staged, "new launcher");
        LinuxInstallationService.SetExecutableMode(installed);
        LinuxInstallationService.SetExecutableMode(staged);

        string oldHash = Hash(installed);
        string newHash = Hash(staged);
        var plan = new LauncherUpdatePlan
        {
            ProductId = LauncherInstallationConstants.ProductId,
            TransactionId = transactionId,
            WaitForProcessId = Environment.ProcessId,
            InstalledExecutable = installed,
            StagedExecutable = staged,
            BackupExecutable = Path.Combine(
                paths.ApplicationDirectory,
                $".{LauncherInstallationConstants.LinuxExecutableFileName}.{transactionId}.previous"),
            ConfirmationFile = Path.Combine(transaction, "confirmed"),
            ExpectedSha256 = newHash,
            ExpectedPreviousSha256 = oldHash,
            Version = "3.1.0",
            ReleaseId = 2,
            Channel = "stable",
            RuntimeIdentifier = "linux-x64"
        };

        LinuxUpdateFileTransaction.Apply(plan);
        Assert(File.ReadAllText(installed) == "new launcher", "Linux update replacement failed.");
        Assert(
            (File.GetUnixFileMode(installed) & UnixFileMode.UserExecute) != 0,
            "Linux update replacement lost executable permission.");
        LinuxUpdateFileTransaction.RollBack(plan);
        Assert(File.ReadAllText(installed) == "old launcher", "Linux update rollback failed.");

        File.Delete(plan.BackupExecutable);
        File.WriteAllText(staged, "corrupt launcher");
        LinuxInstallationService.SetExecutableMode(staged);
        bool rejected = false;
        try
        {
            LinuxUpdateFileTransaction.Apply(plan);
        }
        catch (LauncherException)
        {
            rejected = true;
        }

        Assert(rejected, "A corrupted staged Linux update must be rejected.");
        Assert(
            File.ReadAllText(installed) == "old launcher",
            "Failed Linux update verification corrupted the installed launcher.");
        Assert(
            !File.Exists(plan.BackupExecutable),
            "A pre-replacement Linux update failure left a blocking rollback file.");
    }

    private static PlatformPaths CreateTestLinuxPaths(string root)
    {
        string product = Path.Combine(root, "data", "sunshine-alley");
        string state = Path.Combine(root, "state", "sunshine-alley", "launcher");
        return new PlatformPaths(
            product,
            Path.Combine(product, "launcher"),
            Path.Combine(root, "config", "sunshine-alley", "launcher"),
            Path.Combine(product, "data"),
            Path.Combine(root, "cache", "sunshine-alley", "launcher"),
            state,
            Path.Combine(state, "logs"));
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
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
