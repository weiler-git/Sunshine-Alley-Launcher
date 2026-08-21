namespace SunshineAlley.Platform.Installation;

public static class LauncherInstallationConstants
{
    public const int LayoutVersion = 1;
    public const string ProductId = "SunshineAlleyLauncher";
    public const string ExecutableFileName = "Sunshine Alley Launcher.exe";
    public const string LinuxExecutableFileName = "SunshineAlleyLauncher";
    public const string InstallRegistryPath = @"SOFTWARE\Sunshine Alley\Launcher3";
    public const string UninstallRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SunshineAlleyLauncher";
}

public sealed record InstallationMarker(
    string Product,
    int LayoutVersion,
    string? Version = null,
    string? RuntimeIdentifier = null);

public interface ILauncherInstallationService
{
    PlatformPaths Paths { get; }
    string DefaultExecutablePath { get; }
    bool CanChooseApplicationDirectory { get; }
    bool ShowsWindowsShortcutOptions { get; }
    string UninstallDescription { get; }

    InstallationInspection Inspect();

    Task<InstallationResult> InstallOrRepairAsync(
        InstallationRequest request,
        CancellationToken cancellationToken = default);

    string CreateMaintenanceHelper(
        MaintenanceOperation operation,
        bool removeData,
        bool removeConfiguration,
        IReadOnlyList<string>? legacyFiles = null);

    void StartInstalledCopy(
        string installedExecutable,
        IReadOnlyList<string> legacyFiles);

    string? ReadConfiguredDataDirectory();
    string? ReadConfiguredGameDirectory();
}

public static class LauncherInstallationServiceFactory
{
    public static ILauncherInstallationService Create(PlatformPaths? paths = null)
    {
        paths ??= PlatformPaths.CreateDefault();
        if (OperatingSystem.IsWindows())
        {
            return new WindowsInstallationService(paths);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxInstallationService(paths);
        }

        throw new PlatformNotSupportedException(
            "Guided launcher installation is supported on Windows and Linux.");
    }
}

public sealed record InstallationInspection(
    bool IsInstalled,
    bool IsCurrentExecutable,
    bool IsLegacyLaunch,
    string CurrentExecutable,
    string? RegisteredExecutable,
    string? LegacyDataDirectory,
    string Message);

public sealed record InstallationRequest(
    string ApplicationDirectory,
    string DataDirectory,
    string GameDirectory,
    bool CreateStartMenuShortcut,
    bool CreateDesktopShortcut,
    bool ResetDownloadedData);

public sealed record InstallationResult(
    string InstalledExecutable,
    string DataDirectory,
    IReadOnlyList<string> LegacyFilesToRemove,
    string Message);

public enum MaintenanceOperation
{
    Uninstall,
    CleanupLegacyFiles
}

public sealed class MaintenancePlan
{
    public required string ProductId { get; init; }
    public required string TransactionId { get; init; }
    public required MaintenanceOperation Operation { get; init; }
    public required int WaitForProcessId { get; init; }
    public required string ApplicationDirectory { get; init; }
    public required string InstalledExecutable { get; init; }
    public required string DataDirectory { get; init; }
    public required string ConfigurationDirectory { get; init; }
    public required string CacheDirectory { get; init; }
    public string? StateDirectory { get; init; }
    public string? DesktopEntryPath { get; init; }
    public string? IconPath { get; init; }
    public bool RemoveData { get; init; }
    public bool RemoveConfiguration { get; init; }
    public List<string> LegacyFiles { get; init; } = [];
}

public sealed class LayoutMigrationJournal
{
    public required string ProductId { get; init; }
    public required string SourceRoot { get; init; }
    public required string DestinationRoot { get; init; }
    public List<string> PendingDirectories { get; init; } = [];
    public List<string> CompletedDirectories { get; init; } = [];
}
