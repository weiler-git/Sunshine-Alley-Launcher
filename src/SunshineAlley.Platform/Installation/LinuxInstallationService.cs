using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SunshineAlley.Core;
using SunshineAlley.Platform.Launch;
using SunshineAlley.Platform.Storage;

namespace SunshineAlley.Platform.Installation;

public sealed class LinuxInstallationService : ILauncherInstallationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] MigratedDataDirectories =
    [
        LauncherConstants.ModPacksDirectoryName,
        "Launcher",
        "logs"
    ];

    private readonly PlatformPaths _paths;
    private readonly Func<string> _currentExecutable;

    public LinuxInstallationService(
        PlatformPaths? paths = null,
        Func<string>? currentExecutable = null)
    {
        _paths = paths ?? PlatformPaths.CreateDefault();
        _currentExecutable = currentExecutable ?? GetCurrentExecutable;
    }

    public PlatformPaths Paths => _paths;

    public string DefaultExecutablePath => _paths.LinuxExecutablePath;

    public bool CanChooseApplicationDirectory => false;

    public bool ShowsWindowsShortcutOptions => false;

    public string UninstallDescription =>
        "The installed launcher, application-menu entry, icon, and update cache will be removed. Secret Service credentials are preserved.";

    public InstallationInspection Inspect()
    {
        EnsureLinux();
        string current = Normalize(_currentExecutable());
        string installedExecutable = Normalize(DefaultExecutablePath);
        bool markerValid = IsInstallationMarkerValid(_paths.InstallationMarkerFile);
        bool installed = markerValid
            && File.Exists(installedExecutable)
            && IsExecutable(installedExecutable)
            && !IsFinalPathLink(installedExecutable);
        bool currentInstalled = installed && PathsEqual(current, installedExecutable);
        string? legacyData = FindLegacyDataDirectory();
        bool legacyLaunch = !currentInstalled
            && legacyData is not null
            && PathsEqual(Path.GetDirectoryName(current)!, legacyData);

        string message = currentInstalled
            ? "The installed Sunshine Alley Launcher is running."
            : legacyLaunch
                ? "An earlier Linux data layout was found and can be migrated."
                : installed
                    ? "Sunshine Alley is installed, but this copy is running from another location."
                    : "This Linux copy is not installed for the current user yet.";

        return new InstallationInspection(
            installed,
            currentInstalled,
            legacyLaunch,
            current,
            installed ? installedExecutable : null,
            legacyData,
            message);
    }

    public async Task<InstallationResult> InstallOrRepairAsync(
        InstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureLinux();
        string applicationDirectory = Normalize(request.ApplicationDirectory);
        if (!PathsEqual(applicationDirectory, _paths.ApplicationDirectory))
        {
            throw new LauncherException(
                $"Linux installs to the fixed per-user XDG location '{_paths.ApplicationDirectory}'.");
        }

        string dataDirectory = Normalize(request.DataDirectory);
        string gameDirectory = Normalize(request.GameDirectory);
        ValidateApplicationAndDataPaths(applicationDirectory, dataDirectory);
        InstallationInspection inspection = Inspect();
        string sourceExecutable = Normalize(_currentExecutable());
        ValidatePublishedSource(sourceExecutable);
        PreventDowngrade(inspection, sourceExecutable);

        string? gameExecutable = GameLayoutResolver.FindGameExecutable(gameDirectory);
        if (gameExecutable is null)
        {
            throw new LauncherException(
                "The selected game directory does not contain the native Linux Valheim executable.");
        }

        if (!IsExecutable(gameExecutable))
        {
            throw new LauncherException(
                $"The Valheim executable is not executable: '{gameExecutable}'.");
        }

        DirectoryValidationResult gameAccess = await DirectoryWriteAccess.ValidateAsync(
            gameDirectory,
            "Valheim game directory",
            cancellationToken);
        if (!gameAccess.IsValid)
        {
            throw new LauncherException(gameAccess.Error!);
        }

        var dataPolicy = new ModDataDirectoryPolicy(applicationDirectory);
        DirectoryValidationResult dataValidation = await dataPolicy.ValidateAsync(
            dataDirectory,
            gameDirectory,
            false,
            cancellationToken);
        if (!dataValidation.IsValid)
        {
            throw new LauncherException(dataValidation.Error!);
        }

        _paths.EnsureDirectories();
        string? legacyData = inspection.LegacyDataDirectory;
        if (legacyData is not null && !PathsEqual(legacyData, dataDirectory))
        {
            await MigrateLegacyDataAsync(legacyData, dataDirectory, cancellationToken);
        }

        ValidateDedicatedApplicationDirectory(applicationDirectory);
        DirectoryValidationResult initialized = await dataPolicy.ValidateAsync(
            dataDirectory,
            gameDirectory,
            true,
            cancellationToken);
        if (!initialized.IsValid)
        {
            throw new LauncherException(initialized.Error!);
        }

        if (request.ResetDownloadedData)
        {
            DeleteDownloadedData(initialized.NormalizedPath);
        }

        Directory.CreateDirectory(Path.Combine(initialized.NormalizedPath, "Launcher"));
        Directory.CreateDirectory(Path.Combine(initialized.NormalizedPath, "logs"));
        Directory.CreateDirectory(Path.Combine(
            initialized.NormalizedPath,
            LauncherConstants.ModPacksDirectoryName));

        var settingsStore = new JsonSettingsStore(_paths.SettingsFile);
        var launcherSettings = new LauncherSettings(settingsStore, initialized.NormalizedPath);
        LauncherPreferences existing = await launcherSettings.LoadAsync(cancellationToken);
        await launcherSettings.SaveAsync(
            existing with
            {
                ModDataDirectory = initialized.NormalizedPath,
                GameDirectory = gameDirectory
            },
            cancellationToken);

        Directory.CreateDirectory(applicationDirectory);
        SetPrivateDirectoryMode(applicationDirectory);
        string installedExecutable = DefaultExecutablePath;
        if (!PathsEqual(sourceExecutable, installedExecutable))
        {
            await CopyExecutableAtomicAsync(
                sourceExecutable,
                installedExecutable,
                cancellationToken);
        }
        else
        {
            SetExecutableMode(installedExecutable);
        }

        await WriteInstallationMarkerAsync(cancellationToken);
        await LinuxDesktopIntegration.InstallAsync(
            _paths,
            installedExecutable,
            cancellationToken);

        return new InstallationResult(
            installedExecutable,
            initialized.NormalizedPath,
            [],
            inspection.IsInstalled
                ? "Sunshine Alley Launcher was repaired."
                : inspection.IsLegacyLaunch
                    ? "The earlier Linux data layout was migrated and the launcher was installed."
                    : "Sunshine Alley Launcher was installed for the current user.");
    }

    public string CreateMaintenanceHelper(
        MaintenanceOperation operation,
        bool removeData,
        bool removeConfiguration,
        IReadOnlyList<string>? legacyFiles = null)
    {
        EnsureLinux();
        if (operation != MaintenanceOperation.Uninstall)
        {
            throw new LauncherException(
                "Linux maintenance helper mode currently supports uninstall only.");
        }

        InstallationInspection inspection = Inspect();
        string installedExecutable = inspection.RegisteredExecutable
            ?? throw new LauncherException("No installed Linux launcher was found.");
        _paths.EnsureDirectories();
        string transactionId = Guid.NewGuid().ToString("N");
        string helperRoot = Path.Combine(
            _paths.CacheDirectory,
            "Maintenance",
            transactionId);
        Directory.CreateDirectory(helperRoot);
        SetPrivateDirectoryMode(helperRoot);
        string helperExecutable = Path.Combine(
            helperRoot,
            LauncherInstallationConstants.LinuxExecutableFileName);
        File.Copy(_currentExecutable(), helperExecutable, false);
        SetExecutableMode(helperExecutable);

        var plan = new MaintenancePlan
        {
            ProductId = LauncherInstallationConstants.ProductId,
            TransactionId = transactionId,
            Operation = operation,
            WaitForProcessId = Environment.ProcessId,
            ApplicationDirectory = _paths.ApplicationDirectory,
            InstalledExecutable = installedExecutable,
            DataDirectory = ReadConfiguredDataDirectory() ?? _paths.DataDirectory,
            ConfigurationDirectory = _paths.ConfigurationDirectory,
            CacheDirectory = _paths.CacheDirectory,
            StateDirectory = _paths.StateDirectory,
            DesktopEntryPath = _paths.LinuxDesktopEntryPath,
            IconPath = _paths.LinuxIconPath,
            RemoveData = removeData,
            RemoveConfiguration = removeConfiguration,
            LegacyFiles = legacyFiles?.ToList() ?? []
        };
        string planPath = Path.Combine(helperRoot, "maintenance-plan.json");
        WritePlan(planPath, plan);

        var start = new ProcessStartInfo
        {
            FileName = helperExecutable,
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        start.ArgumentList.Add("--maintenance-helper");
        start.ArgumentList.Add(planPath);
        _ = Process.Start(start)
            ?? throw new LauncherException("The Linux maintenance helper could not be started.");
        return planPath;
    }

    public void StartInstalledCopy(
        string installedExecutable,
        IReadOnlyList<string> legacyFiles)
    {
        EnsureLinux();
        var start = new ProcessStartInfo
        {
            FileName = installedExecutable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installedExecutable)!
        };
        start.ArgumentList.Add("--wait-for-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        _ = Process.Start(start)
            ?? throw new LauncherException("The installed Linux launcher could not be started.");
    }

    public string? ReadConfiguredDataDirectory()
    {
        string? configured = ReadStringSetting(SettingsKeys.ModDataDirectory);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        try
        {
            return PathsEqual(configured, _paths.ApplicationDirectory)
                ? _paths.DataDirectory
                : configured;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    public string? ReadConfiguredGameDirectory() =>
        ReadStringSetting(SettingsKeys.GameDirectory);

    internal static string GetCurrentExecutable() =>
        Environment.ProcessPath
        ?? throw new LauncherException("The current launcher executable path is unavailable.");

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    internal static bool IsInstallationMarkerValid(string markerPath)
    {
        try
        {
            if (!File.Exists(markerPath) || IsFinalPathLink(markerPath))
            {
                return false;
            }

            InstallationMarker? marker = JsonSerializer.Deserialize<InstallationMarker>(
                File.ReadAllText(markerPath),
                JsonOptions);
            return marker?.Product == LauncherInstallationConstants.ProductId
                && marker.LayoutVersion == LauncherInstallationConstants.LayoutVersion
                && (marker.RuntimeIdentifier is null
                    || marker.RuntimeIdentifier.StartsWith("linux-", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return false;
        }
    }

    internal static void SetExecutableMode(string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
    }

    private void ValidateApplicationAndDataPaths(
        string applicationDirectory,
        string dataDirectory)
    {
        ValidateNoOverlap(
            applicationDirectory,
            dataDirectory,
            "The fixed launcher application directory cannot also be used for managed mod data.");
        ValidateNoOverlap(
            applicationDirectory,
            _paths.ConfigurationDirectory,
            "The launcher application and configuration directories must be separate.");
        ValidateNoOverlap(
            applicationDirectory,
            _paths.CacheDirectory,
            "The launcher application and cache directories must be separate.");
        ValidateNoOverlap(
            applicationDirectory,
            _paths.StateDirectory,
            "The launcher application and state directories must be separate.");
        ValidateNoOverlap(
            dataDirectory,
            _paths.ConfigurationDirectory,
            "Managed mod data cannot contain the launcher configuration directory.");
        ValidateNoOverlap(
            dataDirectory,
            _paths.CacheDirectory,
            "Managed mod data cannot contain the launcher cache directory.");
        ValidateNoOverlap(
            dataDirectory,
            _paths.StateDirectory,
            "Managed mod data cannot contain the launcher state directory.");
    }

    private static void ValidateNoOverlap(string first, string second, string message)
    {
        if (PathSecurity.IsUnderRoot(first, second)
            || PathSecurity.IsUnderRoot(second, first))
        {
            throw new LauncherException(message);
        }
    }

    private void ValidateDedicatedApplicationDirectory(string path)
    {
        if (!PathsEqual(path, _paths.ApplicationDirectory))
        {
            throw new LauncherException("The Linux application directory is not the resolved XDG path.");
        }

        if (PathSecurity.ContainsReparsePointUnderRoot(_paths.ProductRoot, path))
        {
            throw new LauncherException(
                "The launcher-owned Linux application path cannot contain a symbolic link.");
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        if (File.Exists(_paths.InstallationMarkerFile)
            && !IsInstallationMarkerValid(_paths.InstallationMarkerFile))
        {
            throw new LauncherException(
                "The Linux application directory contains an unrecognized install.json marker and was not modified.");
        }

        var adoptableNames = new HashSet<string>(StringComparer.Ordinal)
        {
            LauncherInstallationConstants.LinuxExecutableFileName,
            "install.json"
        };
        string[] unknownEntries = Directory
            .EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
            .Where(entry =>
            {
                string name = Path.GetFileName(entry);
                return !adoptableNames.Contains(name)
                    && !name.StartsWith(".SunshineAlleyLauncher.", StringComparison.Ordinal)
                    && !name.StartsWith("SunshineAlleyLauncher.installing.", StringComparison.Ordinal);
            })
            .Take(3)
            .Select(Path.GetFileName)
            .ToArray();
        if (unknownEntries.Length > 0)
        {
            throw new LauncherException(
                "The fixed XDG application directory contains unrelated items and was not modified: "
                + string.Join(", ", unknownEntries));
        }
    }

    private static void ValidatePublishedSource(string sourceExecutable)
    {
        string? entryAssemblyLocation = Assembly.GetEntryAssembly()?.Location;
        if (!File.Exists(sourceExecutable)
            || IsFinalPathLink(sourceExecutable)
            || !string.IsNullOrEmpty(entryAssemblyLocation)
            || !RuntimeInformation.RuntimeIdentifier.StartsWith(
                "linux-",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(
                "Installation requires a published self-contained Linux launcher. Run the downloaded Linux executable, not dotnet run or a framework-dependent build.");
        }
    }

    private static void PreventDowngrade(
        InstallationInspection inspection,
        string sourceExecutable)
    {
        if (!inspection.IsInstalled
            || inspection.RegisteredExecutable is null
            || PathsEqual(sourceExecutable, inspection.RegisteredExecutable))
        {
            return;
        }

        InstallationMarker? marker = ReadInstallationMarker(
            Path.Combine(
                Path.GetDirectoryName(inspection.RegisteredExecutable)!,
                "install.json"));
        if (marker?.Version is null
            || !Version.TryParse(marker.Version, out Version? installedVersion)
            || Assembly.GetEntryAssembly()?.GetName().Version is not Version sourceVersion
            || sourceVersion >= installedVersion)
        {
            return;
        }

        throw new LauncherException(
            $"This setup executable is version {sourceVersion}; the installed Linux launcher is newer ({installedVersion}).");
    }

    private async Task MigrateLegacyDataAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        sourceRoot = Normalize(sourceRoot);
        destinationRoot = Normalize(destinationRoot);
        string marker = Path.Combine(sourceRoot, ModDataDirectoryPolicy.MarkerFileName);
        if (!File.Exists(marker)
            || IsFinalPathLink(marker)
            || !string.Equals(
                await File.ReadAllTextAsync(marker, cancellationToken),
                ModDataDirectoryPolicy.MarkerContents,
                StringComparison.Ordinal)
            || PathSecurity.ContainsReparsePointUnderRoot(_paths.ProductRoot, sourceRoot))
        {
            throw new LauncherException(
                "The earlier Linux data directory is missing its recognized ownership marker and was preserved.");
        }

        Directory.CreateDirectory(destinationRoot);
        if (PathSecurity.ContainsReparsePoint(destinationRoot))
        {
            throw new LauncherException(
                "The selected migration destination traverses a symbolic link and was not modified.");
        }

        foreach (string name in MigratedDataDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = Path.Combine(sourceRoot, name);
            if (!Directory.Exists(source))
            {
                continue;
            }

            if (PathSecurity.ContainsReparsePoint(source))
            {
                throw new LauncherException(
                    $"Legacy Linux data migration rejected linked directory '{source}'.");
            }

            string destination = Path.Combine(destinationRoot, name);
            if (Directory.Exists(destination))
            {
                if (Directory.EnumerateFileSystemEntries(destination).Any())
                {
                    throw new LauncherException(
                        $"Both the earlier and new '{name}' data directories contain files. They were preserved; merge them before retrying setup.");
                }

                Directory.Delete(destination);
            }

            await MoveDirectoryVerifiedAsync(
                source,
                destination,
                cancellationToken);
        }

        string legacySettings = Path.Combine(sourceRoot, "settings.json");
        if (File.Exists(legacySettings))
        {
            if ((File.GetAttributes(legacySettings) & FileAttributes.ReparsePoint) != 0)
            {
                throw new LauncherException(
                    $"Legacy settings file '{legacySettings}' is a symbolic link and was preserved in place.");
            }

            await PreserveLegacySettingsAsync(
                legacySettings,
                destinationRoot,
                cancellationToken);
        }

        File.Delete(marker);
    }

    private static async Task MoveDirectoryVerifiedAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        EnsureTreeContainsNoLinks(source);
        try
        {
            Directory.Move(source, destination);
            return;
        }
        catch (IOException) when (Directory.Exists(source) && !Directory.Exists(destination))
        {
            // XDG data and a user-selected mod-data directory may be different
            // filesystems. Copy into a destination-side staging directory,
            // verify every file, then publish it with a same-filesystem rename.
        }

        string staging = destination + ".migrating";
        if (Directory.Exists(staging))
        {
            EnsureTreeContainsNoLinks(staging);
            Directory.Delete(staging, true);
        }

        Directory.CreateDirectory(staging);
        foreach (string directory in Directory.EnumerateDirectories(
            source,
            "*",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new LauncherException(
                    $"Legacy Linux data migration rejected symbolic link '{directory}'.");
            }

            Directory.CreateDirectory(Path.Combine(
                staging,
                Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(
            source,
            "*",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                throw new LauncherException(
                    $"Legacy Linux data migration rejected symbolic link '{file}'.");
            }

            string copy = Path.Combine(staging, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            await using (FileStream input = new(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(
                copy,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.SetUnixFileMode(copy, File.GetUnixFileMode(file));

            if (!string.Equals(
                    await ComputeSha256Async(file, cancellationToken),
                    await ComputeSha256Async(copy, cancellationToken),
                    StringComparison.Ordinal))
            {
                throw new LauncherException(
                    $"Legacy Linux data migration verification failed for '{file}'. The source was preserved.");
            }
        }

        Directory.Move(staging, destination);
        EnsureTreeContainsNoLinks(source);
        Directory.Delete(source, true);
    }

    private async Task PreserveLegacySettingsAsync(
        string source,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(source, cancellationToken);
        try
        {
            using JsonDocument _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new LauncherException(
                $"Legacy settings file '{source}' is invalid and was preserved in place.",
                exception);
        }

        string destination;
        if (!File.Exists(_paths.SettingsFile))
        {
            Directory.CreateDirectory(_paths.ConfigurationDirectory);
            destination = _paths.SettingsFile;
        }
        else
        {
            string launcherData = Path.Combine(destinationRoot, "Launcher");
            Directory.CreateDirectory(launcherData);
            destination = Path.Combine(launcherData, "legacy-settings.json");
            if (File.Exists(destination))
            {
                destination = Path.Combine(
                    launcherData,
                    $"legacy-settings-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            }
        }

        await CopyFileVerifiedAsync(source, destination, cancellationToken);
        if (LinuxInstallationService.PathsEqual(destination, _paths.SettingsFile))
        {
            File.SetUnixFileMode(
                destination,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Delete(source);
    }

    private static async Task CopyFileVerifiedAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        bool completed = false;
        try
        {
            await using (FileStream input = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            if (!string.Equals(
                    await ComputeSha256Async(source, cancellationToken),
                    await ComputeSha256Async(destination, cancellationToken),
                    StringComparison.Ordinal))
            {
                throw new LauncherException(
                    $"Legacy settings copy verification failed; '{source}' was preserved.");
            }

            completed = true;
        }
        finally
        {
            if (!completed && File.Exists(destination))
            {
                File.Delete(destination);
            }
        }
    }

    private string? FindLegacyDataDirectory()
    {
        string marker = Path.Combine(
            _paths.ApplicationDirectory,
            ModDataDirectoryPolicy.MarkerFileName);
        if (!File.Exists(marker) || IsFinalPathLink(marker))
        {
            return null;
        }

        try
        {
            return string.Equals(
                    File.ReadAllText(marker),
                    ModDataDirectoryPolicy.MarkerContents,
                    StringComparison.Ordinal)
                ? _paths.ApplicationDirectory
                : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? ReadStringSetting(string settingName)
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(_paths.SettingsFile));
            if (document.RootElement.TryGetProperty(settingName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            // Normal settings loading reports the concrete parse/access error.
        }

        return null;
    }

    private async Task WriteInstallationMarkerAsync(CancellationToken cancellationToken)
    {
        await WriteInstallationMarkerAsync(
            _paths,
            GetCurrentVersion(),
            RuntimeInformation.RuntimeIdentifier,
            cancellationToken);
    }

    internal static async Task WriteInstallationMarkerAsync(
        PlatformPaths paths,
        string version,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        var marker = new InstallationMarker(
            LauncherInstallationConstants.ProductId,
            LauncherInstallationConstants.LayoutVersion,
            version,
            runtimeIdentifier);
        await WriteJsonAtomicAsync(
            paths.InstallationMarkerFile,
            marker,
            cancellationToken);
    }

    private static async Task CopyExecutableAtomicAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        string temporary = destination + ".installing." + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream input = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            SetExecutableMode(temporary);
            string sourceHash = await ComputeSha256Async(source, cancellationToken);
            string copiedHash = await ComputeSha256Async(temporary, cancellationToken);
            if (!string.Equals(sourceHash, copiedHash, StringComparison.Ordinal))
            {
                throw new LauncherException(
                    "The copied Linux launcher failed installation hash verification.");
            }

            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void DeleteDownloadedData(string dataRoot)
    {
        string marker = Path.Combine(dataRoot, ModDataDirectoryPolicy.MarkerFileName);
        if (!File.Exists(marker)
            || IsFinalPathLink(marker)
            || !string.Equals(
                File.ReadAllText(marker),
                ModDataDirectoryPolicy.MarkerContents,
                StringComparison.Ordinal)
            || PathSecurity.ContainsReparsePoint(dataRoot))
        {
            throw new LauncherException(
                "Downloaded data can only be reset inside a recognized, non-linked Sunshine Alley data root.");
        }

        string modPacks = Path.Combine(dataRoot, LauncherConstants.ModPacksDirectoryName);
        if (Directory.Exists(modPacks))
        {
            EnsureTreeContainsNoLinks(modPacks);
            Directory.Delete(modPacks, true);
        }
    }

    private static void EnsureTreeContainsNoLinks(string root)
    {
        if (PathSecurity.ContainsReparsePoint(root))
        {
            throw new LauncherException($"Linked directory was not modified: '{root}'.");
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                directory,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new LauncherException(
                        $"Directory tree contains a symbolic link and was not modified: '{entry}'.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        File.SetUnixFileMode(
            temporary,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }

    private static void WritePlan(string path, MaintenancePlan plan)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(plan, JsonOptions),
            new UTF8Encoding(false));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static InstallationMarker? ReadInstallationMarker(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<InstallationMarker>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string GetCurrentVersion()
    {
        string? informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return (informational?.Split('+', 2)[0]
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "0.0.0").Split('-', 2)[0];
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute)) != 0;
    }

    private static bool IsFinalPathLink(string path) =>
        File.Exists(path)
        && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void SetPrivateDirectoryMode(string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Native XDG launcher installation requires Linux.");
        }
    }
}
