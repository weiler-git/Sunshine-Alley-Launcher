using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SunshineAlley.Core;
using SunshineAlley.Platform.Storage;

namespace SunshineAlley.Platform.Installation;

public sealed class WindowsInstallationService : ILauncherInstallationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] MigratedDataDirectories =
    [
        LauncherConstants.ModPacksDirectoryName,
        "Launcher",
        "logs"
    ];

    private readonly PlatformPaths _paths;

    public WindowsInstallationService(PlatformPaths? paths = null) =>
        _paths = paths ?? PlatformPaths.CreateDefault();

    public PlatformPaths Paths => _paths;

    public bool CanChooseApplicationDirectory => true;

    public bool ShowsWindowsShortcutOptions => true;

    public string UninstallDescription =>
        "The application, shortcuts, update cache, and Windows installation registration will be removed. Device identity credentials are preserved.";

    public string DefaultExecutablePath => Path.Combine(
        _paths.ApplicationDirectory,
        LauncherInstallationConstants.ExecutableFileName);

    public InstallationInspection Inspect()
    {
        EnsureWindows();
        string current = GetCurrentExecutable();
        string? registered = ReadRegisteredExecutable();
        bool markerValid = registered is not null
            && IsInstallationMarkerValid(Path.GetDirectoryName(registered)!);
        bool installed = registered is not null && File.Exists(registered) && markerValid;
        bool currentInstalled = installed && PathsEqual(current, registered!);
        string? legacyData = FindLegacyDataDirectory();
        bool legacyLaunch = !currentInstalled && IsLegacyExecutableLocation(current, legacyData);

        string message = currentInstalled
            ? "The installed Sunshine Alley Launcher is running."
            : legacyLaunch
                ? "A legacy Sunshine Alley installation was found and can be migrated."
                : installed
                    ? "Sunshine Alley is installed, but this copy is running from another location."
                    : "This copy is not installed yet.";

        return new InstallationInspection(
            installed,
            currentInstalled,
            legacyLaunch,
            current,
            registered,
            legacyData,
            message);
    }

    public async Task<InstallationResult> InstallOrRepairAsync(
        InstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        string applicationDirectory = Normalize(request.ApplicationDirectory);
        string dataDirectory = Normalize(request.DataDirectory);
        string gameDirectory = Normalize(request.GameDirectory);
        ValidateApplicationAndDataPaths(applicationDirectory, dataDirectory);
        InstallationInspection inspection = Inspect();
        if (inspection.IsInstalled
            && inspection.RegisteredExecutable is not null
            && !PathsEqual(
                applicationDirectory,
                Path.GetDirectoryName(inspection.RegisteredExecutable)!))
        {
            throw new LauncherException(
                "Repair cannot move an existing App directory. Uninstall while preserving Data and Config, then install to the new App location.");
        }

        string? legacyData = inspection.LegacyDataDirectory;
        ValidateLegacyMigrationDestinations(
            legacyData,
            applicationDirectory,
            dataDirectory);

        string sourceExecutable = GetCurrentExecutable();
        string? entryAssemblyLocation = Assembly.GetEntryAssembly()?.Location;
        if (!string.Equals(Path.GetExtension(sourceExecutable), ".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(sourceExecutable)
            || !string.IsNullOrEmpty(entryAssemblyLocation))
        {
            throw new LauncherException(
                "Installation requires the published Windows launcher executable. Run setup from the published win-x64 EXE, not through dotnet run.");
        }

        if (inspection.IsInstalled
            && inspection.RegisteredExecutable is not null
            && !PathsEqual(sourceExecutable, inspection.RegisteredExecutable)
            && Version.TryParse(
                FileVersionInfo.GetVersionInfo(inspection.RegisteredExecutable).FileVersion,
                out Version? installedVersion)
            && Assembly.GetEntryAssembly()?.GetName().Version is Version sourceVersion
            && sourceVersion < installedVersion)
        {
            throw new LauncherException(
                $"This setup executable is version {sourceVersion}; the installed launcher is newer ({installedVersion}). Run the installed copy or use a newer release.");
        }

        string gameExecutable = Path.Combine(gameDirectory, "valheim.exe");
        if (!File.Exists(gameExecutable))
        {
            throw new LauncherException(
                "The selected game directory does not contain valheim.exe.");
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

        Directory.CreateDirectory(applicationDirectory);
        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        Directory.CreateDirectory(_paths.CacheDirectory);
        await MigratePreviousV3ConfigurationAsync(legacyData, cancellationToken);
        if (!string.IsNullOrWhiteSpace(legacyData)
            && Directory.Exists(legacyData)
            && !PathsEqual(legacyData, dataDirectory))
        {
            await MigrateDataLayoutAsync(legacyData, dataDirectory, cancellationToken);
        }

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
        await settingsStore.SetAsync(
            SettingsKeys.WindowsLayoutMigration,
            true,
            cancellationToken);

        string installedExecutable = Path.Combine(
            applicationDirectory,
            LauncherInstallationConstants.ExecutableFileName);
        if (!PathsEqual(sourceExecutable, installedExecutable))
        {
            string temporary = installedExecutable + ".installing";
            File.Copy(sourceExecutable, temporary, true);
            File.Move(temporary, installedExecutable, true);
        }

        await WriteInstallationMarkerAsync(applicationDirectory, cancellationToken);
        RegisterInstallation(applicationDirectory, installedExecutable);
        WindowsShortcutService.Remove();
        WindowsShortcutService.Create(
            installedExecutable,
            request.CreateStartMenuShortcut,
            request.CreateDesktopShortcut);

        List<string> cleanup = FindLegacyExecutableFiles(
            sourceExecutable,
            installedExecutable,
            legacyData);
        return new InstallationResult(
            installedExecutable,
            initialized.NormalizedPath,
            cleanup,
            inspection.IsInstalled
                ? "Sunshine Alley Launcher was repaired."
                : inspection.IsLegacyLaunch
                    ? "The legacy Sunshine Alley installation was migrated."
                    : "Sunshine Alley Launcher was installed.");
    }

    public void RecreateShortcuts(bool startMenu, bool desktop)
    {
        InstallationInspection inspection = Inspect();
        if (!inspection.IsInstalled || inspection.RegisteredExecutable is null)
        {
            throw new LauncherException("No registered Sunshine Alley installation was found.");
        }

        WindowsShortcutService.Remove();
        WindowsShortcutService.Create(inspection.RegisteredExecutable, startMenu, desktop);
    }

    public string CreateMaintenanceHelper(
        MaintenanceOperation operation,
        bool removeData,
        bool removeConfiguration,
        IReadOnlyList<string>? legacyFiles = null)
    {
        InstallationInspection inspection = Inspect();
        string installedExecutable = inspection.RegisteredExecutable
            ?? throw new LauncherException("No registered installation was found.");
        string transactionId = Guid.NewGuid().ToString("N");
        string helperRoot = Path.Combine(
            Path.GetTempPath(),
            "SunshineAlleyLauncher",
            "Maintenance",
            transactionId);
        Directory.CreateDirectory(helperRoot);
        string helperExecutable = Path.Combine(helperRoot, LauncherInstallationConstants.ExecutableFileName);
        File.Copy(GetCurrentExecutable(), helperExecutable, true);

        var plan = new MaintenancePlan
        {
            ProductId = LauncherInstallationConstants.ProductId,
            TransactionId = transactionId,
            Operation = operation,
            WaitForProcessId = Environment.ProcessId,
            ApplicationDirectory = Path.GetDirectoryName(installedExecutable)!,
            InstalledExecutable = installedExecutable,
            DataDirectory = ReadConfiguredDataDirectory() ?? _paths.DataDirectory,
            ConfigurationDirectory = _paths.ConfigurationDirectory,
            CacheDirectory = _paths.CacheDirectory,
            RemoveData = removeData,
            RemoveConfiguration = removeConfiguration,
            LegacyFiles = legacyFiles?.ToList() ?? []
        };
        string planPath = Path.Combine(helperRoot, "maintenance-plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, JsonOptions), new UTF8Encoding(false));

        var start = new ProcessStartInfo
        {
            FileName = helperExecutable,
            UseShellExecute = false,
            WorkingDirectory = helperRoot
        };
        start.ArgumentList.Add("--maintenance-helper");
        start.ArgumentList.Add(planPath);
        _ = Process.Start(start)
            ?? throw new LauncherException("The maintenance helper could not be started.");
        return planPath;
    }

    public void StartInstalledCopy(
        string installedExecutable,
        IReadOnlyList<string> legacyFiles)
    {
        var start = new ProcessStartInfo
        {
            FileName = installedExecutable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installedExecutable)!
        };
        start.ArgumentList.Add("--wait-for-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (string legacyFile in legacyFiles)
        {
            start.ArgumentList.Add("--cleanup-legacy-file");
            start.ArgumentList.Add(legacyFile);
        }

        _ = Process.Start(start)
            ?? throw new LauncherException("The installed launcher could not be started.");
    }

    public string? ReadRegisteredExecutable()
    {
        EnsureWindows();
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            LauncherInstallationConstants.InstallRegistryPath,
            false);
        return key?.GetValue("ExecutablePath") as string;
    }

    public string? ReadConfiguredDataDirectory()
        => ReadStringSetting(SettingsKeys.ModDataDirectory);

    public string? ReadConfiguredGameDirectory()
        => ReadStringSetting(SettingsKeys.GameDirectory) ?? ReadLegacyRegistryString("GameDirectory");

    public bool IsApprovedLegacyCleanupFile(string candidate)
    {
        string file = Normalize(candidate);
        string name = Path.GetFileName(file);
        bool recognizedName = string.Equals(
                name,
                LauncherInstallationConstants.ExecutableFileName,
                StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(
                "Sunshine Alley Launcher.temp.",
                StringComparison.OrdinalIgnoreCase);
        if (!recognizedName || PathSecurity.ContainsReparsePoint(file))
        {
            return false;
        }

        string parent = Path.GetDirectoryName(file)!;
        string? legacyData = ReadLegacyRegistryString("InstallDirectory");
        return PathsEqual(parent, _paths.ProductRoot)
            || (!string.IsNullOrWhiteSpace(legacyData)
                && PathsEqual(parent, legacyData!));
    }

    private string? ReadStringSetting(string settingName)
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFile));
            if (document.RootElement.TryGetProperty(settingName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            // The normal settings loader will present the actionable parse error.
        }

        return null;
    }

    private string? ReadPreviousV3StringSetting(string settingName)
    {
        string[] candidates =
        [
            Path.Combine(_paths.ProductRoot, "Launcher", "settings.json"),
            Path.Combine(_paths.ProductRoot, "settings.json")
        ];
        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path) || PathSecurity.ContainsReparsePoint(path))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty(settingName, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException)
            {
                // A malformed previous test configuration is handled by the
                // explicit settings-migration step, which can report its path.
            }
        }

        return null;
    }

    private static string? ReadLegacyRegistryString(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Sunshine Alley", false);
        return key?.GetValue(valueName) as string;
    }

    public static string GetCurrentExecutable() =>
        Environment.ProcessPath
        ?? throw new LauncherException("The current launcher executable path is unavailable.");

    public static bool PathsEqual(string left, string right) =>
        string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);

    private void ValidateApplicationAndDataPaths(
        string applicationDirectory,
        string dataDirectory)
    {
        ValidateDedicatedApplicationDirectory(applicationDirectory);
        ValidateNoOverlap(
            applicationDirectory,
            dataDirectory,
            "The application and mod-data directories must be separate locations; neither may contain the other.");
        ValidateNoOverlap(
            applicationDirectory,
            _paths.ConfigurationDirectory,
            "The application directory cannot contain or use the fixed Config directory.");
        ValidateNoOverlap(
            applicationDirectory,
            _paths.CacheDirectory,
            "The application directory cannot contain or use the fixed Cache directory.");
        ValidateNoOverlap(
            dataDirectory,
            _paths.ConfigurationDirectory,
            "The mod-data directory cannot contain or use the fixed Config directory.");
        ValidateNoOverlap(
            dataDirectory,
            _paths.CacheDirectory,
            "The mod-data directory cannot contain or use the fixed Cache directory.");
    }

    private static void ValidateNoOverlap(string first, string second, string message)
    {
        if (PathSecurity.IsUnderRoot(first, second)
            || PathSecurity.IsUnderRoot(second, first))
        {
            throw new LauncherException(message);
        }
    }

    private static void ValidateLegacyMigrationDestinations(
        string? legacyRoot,
        string applicationDirectory,
        string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(legacyRoot) || !Directory.Exists(legacyRoot))
        {
            return;
        }

        foreach (string name in MigratedDataDirectories)
        {
            string source = Path.Combine(legacyRoot, name);
            if (!Directory.Exists(source))
            {
                continue;
            }

            if (PathSecurity.IsUnderRoot(source, applicationDirectory)
                || PathSecurity.IsUnderRoot(source, dataDirectory))
            {
                throw new LauncherException(
                    $"The App and Data destinations cannot be inside the legacy '{name}' directory because that directory must be moved during migration.");
            }
        }
    }

    private static void ValidateDedicatedApplicationDirectory(string path)
    {
        if (path.Length >= 2
            && IsDirectorySeparator(path[0])
            && IsDirectorySeparator(path[1]))
        {
            throw new LauncherException(
                "The Windows application directory must be on a local drive so updates can be replaced atomically.");
        }

        string? root = Path.GetPathRoot(path);
        if (root is null || PathsEqual(root, path))
        {
            throw new LauncherException("A filesystem root cannot be used as the application directory.");
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)
            && (PathsEqual(path, profile) || PathSecurity.IsUnderRoot(path, profile)))
        {
            throw new LauncherException(
                "Choose a dedicated application directory rather than the user profile or one of its parent directories.");
        }

        string[] protectedDirectories =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(profile, "Downloads"),
            Path.GetTempPath()
        ];
        foreach (string protectedDirectory in protectedDirectories.Where(value =>
            !string.IsNullOrWhiteSpace(value)))
        {
            if (PathSecurity.IsUnderRoot(protectedDirectory, path)
                || PathSecurity.IsUnderRoot(path, protectedDirectory))
            {
                throw new LauncherException(
                    "The application directory cannot be Documents, Desktop, Downloads, the temporary-files directory, or a directory inside one of them.");
            }
        }

        string[] systemDirectories =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        ];
        if (systemDirectories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(directory => PathSecurity.IsUnderRoot(directory, path)))
        {
            throw new LauncherException(
                "The per-user launcher cannot be installed inside Windows, Program Files, or ProgramData. Choose a user-writable local directory.");
        }

        if (PathSecurity.ContainsReparsePoint(path))
        {
            throw new LauncherException(
                "The application directory cannot traverse a symbolic link or Windows junction.");
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        string marker = Path.Combine(path, "install.json");
        if (File.Exists(marker))
        {
            if (!IsInstallationMarkerValid(path))
            {
                throw new LauncherException(
                    "The application directory contains an unrecognized install.json marker and was not modified.");
            }

            return;
        }

        var adoptableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LauncherInstallationConstants.ExecutableFileName,
            LauncherInstallationConstants.ExecutableFileName + ".installing"
        };
        string[] unknownEntries = Directory
            .EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
            .Where(entry => !adoptableNames.Contains(Path.GetFileName(entry)))
            .Take(3)
            .Select(Path.GetFileName)
            .ToArray();
        if (unknownEntries.Length > 0)
        {
            throw new LauncherException(
                "Choose an empty application directory or the existing Sunshine Alley App directory. "
                + "This directory contains unrelated items: "
                + string.Join(", ", unknownEntries));
        }
    }

    private async Task MigratePreviousV3ConfigurationAsync(
        string? legacyDataDirectory,
        CancellationToken cancellationToken)
    {
        if (File.Exists(_paths.SettingsFile))
        {
            return;
        }

        var candidates = new List<string>
        {
            Path.Combine(_paths.ProductRoot, "Launcher", "settings.json"),
            Path.Combine(_paths.ProductRoot, "settings.json")
        };
        if (!string.IsNullOrWhiteSpace(legacyDataDirectory))
        {
            candidates.Add(Path.Combine(
                legacyDataDirectory,
                "Launcher",
                "settings.json"));
            candidates.Add(Path.Combine(legacyDataDirectory, "settings.json"));
        }

        string? source = candidates.FirstOrDefault(File.Exists);
        if (source is null || PathSecurity.ContainsReparsePoint(source))
        {
            return;
        }

        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        string json = await File.ReadAllTextAsync(source, cancellationToken);
        try
        {
            using JsonDocument _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new LauncherException(
                $"Legacy settings file '{source}' is not valid JSON and was not migrated.",
                exception);
        }

        string temporary = Path.Combine(
            _paths.ConfigurationDirectory,
            $".settings-migration-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                json,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, _paths.SettingsFile, false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async Task MigrateDataLayoutAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        sourceRoot = Normalize(sourceRoot);
        destinationRoot = Normalize(destinationRoot);
        if (PathSecurity.ContainsReparsePoint(sourceRoot)
            || PathSecurity.ContainsReparsePoint(destinationRoot))
        {
            throw new LauncherException(
                "Legacy data cannot be migrated through a symbolic link or Windows junction.");
        }

        Directory.CreateDirectory(destinationRoot);
        string journalPath = Path.Combine(
            _paths.ConfigurationDirectory,
            "layout-migration-v2.json");
        LayoutMigrationJournal journal = await LoadOrCreateJournalAsync(
            journalPath,
            sourceRoot,
            destinationRoot,
            cancellationToken);

        foreach (string name in journal.PendingDirectories.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = Path.Combine(sourceRoot, name);
            string destination = Path.Combine(destinationRoot, name);
            if (Directory.Exists(source))
            {
                EnsureTreeContainsNoReparsePoints(source);
                if (Directory.Exists(destination)
                    && Directory.EnumerateFileSystemEntries(destination).Any())
                {
                    if (!await TreesAreEquivalentAsync(source, destination, cancellationToken))
                    {
                        throw new LauncherException(
                            $"Migration stopped because both the old and new '{name}' directories contain different data. Resolve the conflict and run setup again.");
                    }

                    EnsureTreeContainsNoReparsePoints(source);
                    Directory.Delete(source, true);
                }
                else
                {
                    if (Directory.Exists(destination))
                    {
                        Directory.Delete(destination);
                    }

                    bool sameVolume = string.Equals(
                        Path.GetPathRoot(source),
                        Path.GetPathRoot(destination),
                        StringComparison.OrdinalIgnoreCase);
                    if (sameVolume)
                    {
                        Directory.Move(source, destination);
                    }
                    else
                    {
                        string staging = destination + ".migrating";
                        if (Directory.Exists(staging))
                        {
                            EnsureTreeContainsNoReparsePoints(staging);
                            Directory.Delete(staging, true);
                        }

                        await CopyTreeAsync(source, staging, cancellationToken);
                        Directory.Move(staging, destination);
                        if (!await TreesAreEquivalentAsync(source, destination, cancellationToken))
                        {
                            throw new LauncherException(
                                $"The copied '{name}' directory failed migration verification; the original was preserved.");
                        }

                        EnsureTreeContainsNoReparsePoints(source);
                        Directory.Delete(source, true);
                    }
                }
            }

            journal.PendingDirectories.Remove(name);
            if (!journal.CompletedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                journal.CompletedDirectories.Add(name);
            }

            await SaveJsonAtomicAsync(journalPath, journal, cancellationToken);
        }

        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }
    }

    private static async Task<LayoutMigrationJournal> LoadOrCreateJournalAsync(
        string journalPath,
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        if (File.Exists(journalPath))
        {
            await using FileStream input = File.OpenRead(journalPath);
            LayoutMigrationJournal? existing = await JsonSerializer.DeserializeAsync<
                LayoutMigrationJournal>(input, JsonOptions, cancellationToken);
            if (existing is null
                || existing.ProductId != LauncherInstallationConstants.ProductId
                || !PathsEqual(existing.SourceRoot, sourceRoot)
                || !PathsEqual(existing.DestinationRoot, destinationRoot))
            {
                throw new LauncherException(
                    "An incomplete layout migration exists for different directories. Resolve or remove the migration journal before retrying.");
            }

            return existing;
        }

        var journal = new LayoutMigrationJournal
        {
            ProductId = LauncherInstallationConstants.ProductId,
            SourceRoot = sourceRoot,
            DestinationRoot = destinationRoot,
            PendingDirectories = MigratedDataDirectories
                .Where(name => Directory.Exists(Path.Combine(sourceRoot, name)))
                .ToList()
        };
        await SaveJsonAtomicAsync(journalPath, journal, cancellationToken);
        return journal;
    }

    private static void DeleteDownloadedData(string dataRoot)
    {
        string marker = Path.Combine(dataRoot, ModDataDirectoryPolicy.MarkerFileName);
        if (!File.Exists(marker) || PathSecurity.ContainsReparsePoint(dataRoot))
        {
            throw new LauncherException(
                "Downloaded data can only be reset inside a recognized, non-linked Sunshine Alley data root.");
        }

        string modPacks = Path.Combine(dataRoot, LauncherConstants.ModPacksDirectoryName);
        if (Directory.Exists(modPacks))
        {
            EnsureTreeContainsNoReparsePoints(modPacks);
            Directory.Delete(modPacks, true);
        }
    }

    private static async Task CopyTreeAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        EnsureTreeContainsNoReparsePoints(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        foreach ((string path, bool isDirectory) in EnumerateTreeEntries(sourceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isDirectory)
            {
                Directory.CreateDirectory(Path.Combine(
                    destinationRoot,
                    Path.GetRelativePath(sourceRoot, path)));
                continue;
            }

            string source = path;
            string destination = Path.Combine(
                destinationRoot,
                Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using FileStream input = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using FileStream output = new(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
    }

    private static async Task<bool> TreesAreEquivalentAsync(
        string leftRoot,
        string rightRoot,
        CancellationToken cancellationToken)
    {
        EnsureTreeContainsNoReparsePoints(leftRoot);
        EnsureTreeContainsNoReparsePoints(rightRoot);
        string[] leftFiles = EnumerateTreeEntries(leftRoot)
            .Where(item => !item.IsDirectory)
            .Select(item => Path.GetRelativePath(leftRoot, item.Path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] rightFiles = EnumerateTreeEntries(rightRoot)
            .Where(item => !item.IsDirectory)
            .Select(item => Path.GetRelativePath(rightRoot, item.Path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!leftFiles.SequenceEqual(rightFiles, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string relative in leftFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string left = Path.Combine(leftRoot, relative);
            string right = Path.Combine(rightRoot, relative);
            if (new FileInfo(left).Length != new FileInfo(right).Length
                || !string.Equals(
                    await ComputeSha256Async(left, cancellationToken),
                    await ComputeSha256Async(right, cancellationToken),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(
            stream,
            cancellationToken));
    }

    private static void EnsureTreeContainsNoReparsePoints(string root)
    {
        if (PathSecurity.ContainsReparsePoint(root))
        {
            throw new LauncherException($"Migration rejected linked path '{root}'.");
        }

        foreach (var _ in EnumerateTreeEntries(root))
        {
        }
    }

    private static IEnumerable<(string Path, bool IsDirectory)> EnumerateTreeEntries(
        string root)
    {
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
                        $"Migration rejected symbolic link or junction '{entry}'.");
                }

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                yield return (entry, isDirectory);
                if (isDirectory)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private string? FindLegacyDataDirectory()
    {
        if (ReadBooleanSetting(SettingsKeys.WindowsLayoutMigration) == true)
        {
            return null;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Sunshine Alley", false);
        if (key?.GetValue("InstallDirectory") is string configured
            && !string.IsNullOrWhiteSpace(configured)
            && Directory.Exists(configured))
        {
            return Normalize(configured);
        }

        string? previousV3Data = ReadPreviousV3StringSetting(
            SettingsKeys.ModDataDirectory);
        if (!string.IsNullOrWhiteSpace(previousV3Data)
            && Directory.Exists(previousV3Data))
        {
            return Normalize(previousV3Data);
        }

        string previousV3 = Path.Combine(_paths.ProductRoot, "Launcher");
        if (Directory.Exists(Path.Combine(previousV3, LauncherConstants.ModPacksDirectoryName)))
        {
            return previousV3;
        }

        if (Directory.Exists(Path.Combine(_paths.ProductRoot, LauncherConstants.ModPacksDirectoryName)))
        {
            return _paths.ProductRoot;
        }

        return null;
    }

    private bool? ReadBooleanSetting(string settingName)
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFile));
            if (document.RootElement.TryGetProperty(settingName, out JsonElement value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
                    _ => null
                };
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private bool IsLegacyExecutableLocation(string currentExecutable, string? legacyData)
    {
        string currentDirectory = Path.GetDirectoryName(currentExecutable)!;
        return PathsEqual(currentDirectory, _paths.ProductRoot)
            || (!string.IsNullOrWhiteSpace(legacyData)
                && PathsEqual(currentDirectory, legacyData));
    }

    private static bool IsInstallationMarkerValid(string applicationDirectory)
    {
        string markerPath = Path.Combine(applicationDirectory, "install.json");
        try
        {
            InstallationMarker? marker = JsonSerializer.Deserialize<InstallationMarker>(
                File.ReadAllText(markerPath),
                JsonOptions);
            return marker?.Product == LauncherInstallationConstants.ProductId
                && marker.LayoutVersion == LauncherInstallationConstants.LayoutVersion;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return false;
        }
    }

    private static async Task WriteInstallationMarkerAsync(
        string applicationDirectory,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(applicationDirectory, "install.json");
        var marker = new InstallationMarker(
            LauncherInstallationConstants.ProductId,
            LauncherInstallationConstants.LayoutVersion);
        await SaveJsonAtomicAsync(path, marker, cancellationToken);
    }

    private static async Task SaveJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        File.Move(temporary, path, true);
    }

    private static void RegisterInstallation(string applicationDirectory, string executable)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
            LauncherInstallationConstants.InstallRegistryPath))
        {
            key.SetValue("InstallLocation", applicationDirectory, RegistryValueKind.String);
            key.SetValue("ExecutablePath", executable, RegistryValueKind.String);
            key.SetValue("LayoutVersion", LauncherInstallationConstants.LayoutVersion, RegistryValueKind.DWord);
        }

        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "3.0.0";
        using RegistryKey uninstall = Registry.CurrentUser.CreateSubKey(
            LauncherInstallationConstants.UninstallRegistryPath);
        uninstall.SetValue("DisplayName", "Sunshine Alley Launcher", RegistryValueKind.String);
        uninstall.SetValue("DisplayVersion", version, RegistryValueKind.String);
        uninstall.SetValue("Publisher", "Sunshine Alley", RegistryValueKind.String);
        uninstall.SetValue("InstallLocation", applicationDirectory, RegistryValueKind.String);
        uninstall.SetValue("DisplayIcon", executable, RegistryValueKind.String);
        uninstall.SetValue("UninstallString", $"\"{executable}\" --uninstall", RegistryValueKind.String);
        uninstall.SetValue("ModifyPath", $"\"{executable}\" --repair", RegistryValueKind.String);
        uninstall.SetValue("NoModify", 0, RegistryValueKind.DWord);
        uninstall.SetValue("NoRepair", 0, RegistryValueKind.DWord);
    }

    private List<string> FindLegacyExecutableFiles(
        string current,
        string installed,
        string? legacyData)
    {
        var result = new List<string>();
        if (!PathsEqual(current, installed)
            && (PathsEqual(Path.GetDirectoryName(current)!, _paths.ProductRoot)
                || (!string.IsNullOrWhiteSpace(legacyData)
                    && PathsEqual(Path.GetDirectoryName(current)!, legacyData!))))
        {
            result.Add(current);
        }

        string[] cleanupRoots = string.IsNullOrWhiteSpace(legacyData)
            ? [_paths.ProductRoot]
            : [_paths.ProductRoot, legacyData!];
        foreach (string root in cleanupRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root) || PathSecurity.ContainsReparsePoint(root))
            {
                continue;
            }

            result.AddRange(Directory.EnumerateFiles(
                root,
                "Sunshine Alley Launcher.temp.*.exe",
                SearchOption.TopDirectoryOnly));
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsDirectorySeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Guided installation is currently implemented for Windows only.");
        }
    }
}
