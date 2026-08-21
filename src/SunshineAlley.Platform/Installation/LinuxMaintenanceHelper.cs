using System.Diagnostics;
using System.Text.Json;
using SunshineAlley.Core;
using SunshineAlley.Platform.Storage;

namespace SunshineAlley.Platform.Installation;

public static class LinuxMaintenanceHelper
{
    public static async Task<int?> TryRunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        int marker = IndexOf(arguments, "--maintenance-helper");
        if (marker < 0)
        {
            return null;
        }

        if (!OperatingSystem.IsLinux() || marker + 1 >= arguments.Count)
        {
            return 2;
        }

        try
        {
            string planPath = Path.GetFullPath(arguments[marker + 1]);
            MaintenancePlan plan = JsonSerializer.Deserialize<MaintenancePlan>(
                await File.ReadAllTextAsync(planPath, cancellationToken))
                ?? throw new LauncherException("The Linux maintenance plan is empty.");
            Validate(planPath, plan);
            await WaitForExitAsync(plan.WaitForProcessId, cancellationToken);
            Uninstall(plan);
            return 0;
        }
        catch (Exception exception)
        {
            await WriteFailureLogAsync(exception);
            return 1;
        }
    }

    private static void Validate(string planPath, MaintenancePlan plan)
    {
        PlatformPaths paths = PlatformPaths.CreateDefault();
        if (!string.Equals(
                plan.ProductId,
                LauncherInstallationConstants.ProductId,
                StringComparison.Ordinal)
            || !Guid.TryParseExact(plan.TransactionId, "N", out _)
            || plan.Operation != MaintenanceOperation.Uninstall
            || plan.WaitForProcessId <= 0)
        {
            throw new LauncherException("The Linux maintenance plan identity is invalid.");
        }

        string transactionRoot = Path.Combine(
            paths.CacheDirectory,
            "Maintenance",
            plan.TransactionId);
        string expectedPlan = Path.Combine(transactionRoot, "maintenance-plan.json");
        string expectedHelper = Path.Combine(
            transactionRoot,
            LauncherInstallationConstants.LinuxExecutableFileName);
        if (!LinuxInstallationService.PathsEqual(planPath, expectedPlan)
            || PathSecurity.ContainsReparsePointUnderRoot(
                paths.CacheDirectory,
                planPath)
            || !LinuxInstallationService.PathsEqual(
                LinuxInstallationService.GetCurrentExecutable(),
                expectedHelper)
            || PathSecurity.ContainsReparsePointUnderRoot(
                paths.CacheDirectory,
                expectedHelper)
            || PathSecurity.ContainsReparsePointUnderRoot(
                paths.CacheDirectory,
                transactionRoot)
            || !LinuxInstallationService.PathsEqual(
                plan.ApplicationDirectory,
                paths.ApplicationDirectory)
            || !LinuxInstallationService.PathsEqual(
                plan.InstalledExecutable,
                paths.LinuxExecutablePath)
            || !LinuxInstallationService.PathsEqual(
                plan.ConfigurationDirectory,
                paths.ConfigurationDirectory)
            || !LinuxInstallationService.PathsEqual(
                plan.CacheDirectory,
                paths.CacheDirectory)
            || plan.StateDirectory is null
            || !LinuxInstallationService.PathsEqual(
                plan.StateDirectory,
                paths.StateDirectory)
            || plan.DesktopEntryPath is null
            || !LinuxInstallationService.PathsEqual(
                plan.DesktopEntryPath,
                paths.LinuxDesktopEntryPath)
            || plan.IconPath is null
            || !LinuxInstallationService.PathsEqual(
                plan.IconPath,
                paths.LinuxIconPath)
            || !LinuxInstallationService.IsInstallationMarkerValid(
                paths.InstallationMarkerFile))
        {
            throw new LauncherException(
                "The Linux maintenance plan does not match the current XDG installation.");
        }

        if (plan.RemoveData)
        {
            ValidateDataSeparation(plan.DataDirectory, paths);
        }
    }

    private static void ValidateDataSeparation(string dataDirectory, PlatformPaths paths)
    {
        string data = Path.GetFullPath(dataDirectory);
        string[] protectedDirectories =
        [
            paths.ApplicationDirectory,
            paths.ConfigurationDirectory,
            paths.CacheDirectory,
            paths.StateDirectory
        ];
        if (protectedDirectories.Any(directory =>
            PathSecurity.IsUnderRoot(data, directory)
            || PathSecurity.IsUnderRoot(directory, data)))
        {
            throw new LauncherException(
                "The configured mod-data path overlaps a fixed launcher directory and was not removed.");
        }
    }

    private static void Uninstall(MaintenancePlan plan)
    {
        PlatformPaths paths = PlatformPaths.CreateDefault();
        ValidateOwnedTree(paths.ApplicationDirectory, OwnershipMarker.Installation);
        ValidateOwnedTree(paths.CacheDirectory, OwnershipMarker.None);
        ValidateOwnedTree(paths.StateDirectory, OwnershipMarker.None);
        if (plan.RemoveConfiguration)
        {
            ValidateOwnedTree(paths.ConfigurationDirectory, OwnershipMarker.None);
        }

        if (plan.RemoveData)
        {
            ValidateOwnedTree(plan.DataDirectory, OwnershipMarker.ModData);
        }

        LinuxDesktopIntegration.Remove(paths);
        if (plan.RemoveData)
        {
            DeleteOwnedTree(plan.DataDirectory, OwnershipMarker.ModData);
        }

        if (plan.RemoveConfiguration)
        {
            DeleteOwnedTree(paths.ConfigurationDirectory, OwnershipMarker.None);
        }

        DeleteOwnedTree(paths.ApplicationDirectory, OwnershipMarker.Installation);
        DeleteOwnedTree(paths.StateDirectory, OwnershipMarker.None);
        DeleteOwnedTree(paths.CacheDirectory, OwnershipMarker.None);
        TryDeleteEmpty(paths.ProductRoot);
    }

    private static string ValidateOwnedTree(string path, OwnershipMarker marker)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? root = Path.GetPathRoot(normalized);
        if (root is null || LinuxInstallationService.PathsEqual(root, normalized))
        {
            throw new LauncherException("Refusing to remove a filesystem root.");
        }

        if (!Directory.Exists(normalized))
        {
            return normalized;
        }

        if ((File.GetAttributes(normalized) & FileAttributes.ReparsePoint) != 0)
        {
            throw new LauncherException(
                $"Refusing to remove linked directory '{normalized}'.");
        }

        if (marker == OwnershipMarker.Installation
            && !LinuxInstallationService.IsInstallationMarkerValid(
                Path.Combine(normalized, "install.json")))
        {
            throw new LauncherException(
                "The Linux application ownership marker is missing; the application was preserved.");
        }

        if (marker == OwnershipMarker.ModData)
        {
            string dataMarker = Path.Combine(
                normalized,
                ModDataDirectoryPolicy.MarkerFileName);
            if (!File.Exists(dataMarker)
                || !string.Equals(
                    File.ReadAllText(dataMarker),
                    ModDataDirectoryPolicy.MarkerContents,
                    StringComparison.Ordinal))
            {
                throw new LauncherException(
                    "The mod-data ownership marker is missing; managed data was preserved.");
            }
        }

        EnsureTreeContainsNoLinks(normalized);
        return normalized;
    }

    private static void DeleteOwnedTree(string path, OwnershipMarker marker)
    {
        string normalized = ValidateOwnedTree(path, marker);
        if (Directory.Exists(normalized))
        {
            Directory.Delete(normalized, true);
        }
    }

    private static void EnsureTreeContainsNoLinks(string root)
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
                        $"Refusing to remove a tree containing symbolic link '{entry}'.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static async Task WaitForExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(
                TimeSpan.FromMinutes(2),
                cancellationToken);
        }
        catch (ArgumentException)
        {
            // The launcher process already exited.
        }
    }

    private static void TryDeleteEmpty(string path)
    {
        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0
            && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private static async Task WriteFailureLogAsync(Exception exception)
    {
        try
        {
            PlatformPaths paths = PlatformPaths.CreateDefault();
            Directory.CreateDirectory(paths.LogDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(
                    paths.LogDirectory,
                    $"maintenance-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log"),
                exception.ToString());
        }
        catch
        {
            // The helper exit code remains the primary diagnostic.
        }
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string value)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private enum OwnershipMarker
    {
        None,
        Installation,
        ModData
    }
}
