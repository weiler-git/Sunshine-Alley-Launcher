using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using SunshineAlley.Core;
using SunshineAlley.Platform.Storage;

namespace SunshineAlley.Platform.Installation;

public static class WindowsMaintenanceHelper
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

        if (!OperatingSystem.IsWindows() || marker + 1 >= arguments.Count)
        {
            return 2;
        }

        try
        {
            string planPath = Path.GetFullPath(arguments[marker + 1]);
            ValidatePlanLocation(planPath);
            MaintenancePlan plan = JsonSerializer.Deserialize<MaintenancePlan>(
                await File.ReadAllTextAsync(planPath, cancellationToken))
                ?? throw new LauncherException("The maintenance plan is empty.");
            ValidateTransactionPaths(planPath, plan);
            ValidatePlan(plan);
            await WaitForExitAsync(plan.WaitForProcessId, cancellationToken);

            if (plan.Operation == MaintenanceOperation.CleanupLegacyFiles)
            {
                CleanupLegacyFiles(plan);
            }
            else
            {
                Uninstall(plan);
            }

            return 0;
        }
        catch (Exception exception)
        {
            await WriteFailureLogAsync(exception);
            return 1;
        }
    }

    private static void ValidatePlanLocation(string planPath)
    {
        string maintenanceRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "SunshineAlleyLauncher",
            "Maintenance"));
        if (!PathSecurity.IsUnderRoot(maintenanceRoot, planPath)
            || PathSecurity.ContainsReparsePoint(planPath))
        {
            throw new LauncherException("The maintenance plan is outside the protected helper directory.");
        }
    }

    private static void ValidatePlan(MaintenancePlan plan)
    {
        if (!string.Equals(
                plan.ProductId,
                LauncherInstallationConstants.ProductId,
                StringComparison.Ordinal)
            || !Guid.TryParseExact(plan.TransactionId, "N", out _)
            || !Enum.IsDefined(plan.Operation)
            || plan.WaitForProcessId <= 0)
        {
            throw new LauncherException("The maintenance plan identity is invalid.");
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            LauncherInstallationConstants.InstallRegistryPath,
            false);
        string? registered = key?.GetValue("ExecutablePath") as string;
        if (registered is null
            || !WindowsInstallationService.PathsEqual(registered, plan.InstalledExecutable)
            || !WindowsInstallationService.PathsEqual(
                Path.GetDirectoryName(registered)!,
                plan.ApplicationDirectory))
        {
            throw new LauncherException("The maintenance target does not match the registered installation.");
        }

        string marker = Path.Combine(plan.ApplicationDirectory, "install.json");
        if (!File.Exists(marker) || PathSecurity.ContainsReparsePoint(plan.ApplicationDirectory))
        {
            throw new LauncherException("The registered application marker is missing or unsafe.");
        }

        InstallationMarker? installationMarker = JsonSerializer.Deserialize<InstallationMarker>(
            File.ReadAllText(marker));
        if (installationMarker?.Product != LauncherInstallationConstants.ProductId
            || installationMarker.LayoutVersion != LauncherInstallationConstants.LayoutVersion)
        {
            throw new LauncherException("The registered application marker is not recognized.");
        }
    }

    private static void ValidateTransactionPaths(string planPath, MaintenancePlan plan)
    {
        if (!Guid.TryParseExact(plan.TransactionId, "N", out _))
        {
            throw new LauncherException("The maintenance transaction identity is invalid.");
        }

        string transactionRoot = Path.Combine(
            Path.GetTempPath(),
            "SunshineAlleyLauncher",
            "Maintenance",
            plan.TransactionId);
        string expectedPlan = Path.Combine(transactionRoot, "maintenance-plan.json");
        string expectedHelper = Path.Combine(
            transactionRoot,
            LauncherInstallationConstants.ExecutableFileName);
        if (!WindowsInstallationService.PathsEqual(planPath, expectedPlan)
            || !WindowsInstallationService.PathsEqual(
                WindowsInstallationService.GetCurrentExecutable(),
                expectedHelper)
            || PathSecurity.ContainsReparsePoint(transactionRoot)
            || PathSecurity.ContainsReparsePoint(expectedHelper))
        {
            throw new LauncherException(
                "Maintenance helper mode can run only from its protected temporary transaction directory.");
        }
    }

    private static async Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
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
            // The process already exited.
        }
    }

    private static void CleanupLegacyFiles(MaintenancePlan plan)
    {
        string defaultProductRoot = PlatformPaths.CreateDefault().ProductRoot;
        foreach (string candidate in plan.LegacyFiles)
        {
            string file = Path.GetFullPath(candidate);
            string name = Path.GetFileName(file);
            if (!PathSecurity.IsUnderRoot(defaultProductRoot, file)
                || WindowsInstallationService.PathsEqual(file, plan.InstalledExecutable)
                || !(string.Equals(
                        name,
                        LauncherInstallationConstants.ExecutableFileName,
                        StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(
                        "Sunshine Alley Launcher.temp.",
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (File.Exists(file) && !PathSecurity.ContainsReparsePoint(file))
            {
                File.Delete(file);
            }
        }
    }

    private static void Uninstall(MaintenancePlan plan)
    {
        PlatformPaths defaults = PlatformPaths.CreateDefault();
        bool removeConfiguration = plan.RemoveConfiguration
            && WindowsInstallationService.PathsEqual(
                plan.ConfigurationDirectory,
                defaults.ConfigurationDirectory);
        bool removeCache = WindowsInstallationService.PathsEqual(
            plan.CacheDirectory,
            defaults.CacheDirectory);

        // Validate every requested tree before deleting the application that is
        // needed to retry repair/uninstall after a marker or link error.
        ValidateTreeForDeletion(plan.ApplicationDirectory, requireDataMarker: false);
        if (plan.RemoveData)
        {
            ValidateTreeForDeletion(plan.DataDirectory, requireDataMarker: true);
        }

        if (removeConfiguration)
        {
            ValidateTreeForDeletion(plan.ConfigurationDirectory, requireDataMarker: false);
        }

        if (removeCache)
        {
            ValidateTreeForDeletion(plan.CacheDirectory, requireDataMarker: false);
        }

        WindowsShortcutService.Remove();
        if (plan.RemoveData)
        {
            DeleteTree(plan.DataDirectory, requireDataMarker: true);
        }

        if (removeConfiguration)
        {
            DeleteTree(plan.ConfigurationDirectory, requireDataMarker: false);
        }

        if (removeCache)
        {
            DeleteTree(plan.CacheDirectory, requireDataMarker: false);
        }

        DeleteTree(plan.ApplicationDirectory, requireDataMarker: false);

        Registry.CurrentUser.DeleteSubKeyTree(
            LauncherInstallationConstants.InstallRegistryPath,
            false);
        Registry.CurrentUser.DeleteSubKeyTree(
            LauncherInstallationConstants.UninstallRegistryPath,
            false);
    }

    private static void DeleteTree(string path, bool requireDataMarker)
    {
        string normalized = ValidateTreeForDeletion(path, requireDataMarker);
        if (Directory.Exists(normalized))
        {
            Directory.Delete(normalized, true);
        }
    }

    private static string ValidateTreeForDeletion(string path, bool requireDataMarker)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? root = Path.GetPathRoot(normalized);
        if (root is null || WindowsInstallationService.PathsEqual(root, normalized))
        {
            throw new LauncherException("Refusing to remove a filesystem root.");
        }

        if (!Directory.Exists(normalized))
        {
            return normalized;
        }

        if (requireDataMarker
            && !File.Exists(Path.Combine(normalized, ModDataDirectoryPolicy.MarkerFileName)))
        {
            throw new LauncherException(
                "The data directory ownership marker is missing; mod data was preserved.");
        }

        EnsureTreeContainsNoReparsePoints(normalized);
        return normalized;
    }

    private static void EnsureTreeContainsNoReparsePoints(string root)
    {
        if (PathSecurity.ContainsReparsePoint(root))
        {
            throw new LauncherException($"Refusing to remove a linked directory: '{root}'.");
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
                        $"Refusing to remove a directory tree containing a link or junction: '{entry}'.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static async Task WriteFailureLogAsync(Exception exception)
    {
        try
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sunshine Alley",
                "MaintenanceFailures");
            Directory.CreateDirectory(logDirectory);
            string log = Path.Combine(
                logDirectory,
                $"maintenance-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            await File.WriteAllTextAsync(log, exception.ToString());
        }
        catch
        {
            // The original maintenance failure remains the meaningful result.
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
}
