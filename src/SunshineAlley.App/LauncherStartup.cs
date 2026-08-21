using System.Diagnostics;
using SunshineAlley.App.ViewModels;
using SunshineAlley.Core;
using SunshineAlley.Platform;
using SunshineAlley.Platform.Installation;

namespace SunshineAlley.App;

internal sealed record LauncherStartup(
    SetupIntent? SetupIntent,
    string? PostUpdatePlan,
    bool WasUpdateRollback,
    string? RolledBackUpdatePlan,
    bool Portable)
{
    public static LauncherStartup Current { get; private set; } = new(
        null,
        null,
        false,
        null,
        false);

    public static void Initialize(IReadOnlyList<string> arguments)
    {
        SetupIntent? intent = arguments.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)
            ? ViewModels.SetupIntent.Uninstall
            : arguments.Contains("--repair", StringComparer.OrdinalIgnoreCase)
                ? ViewModels.SetupIntent.Repair
                : null;
        string? postUpdatePlan = ReadOption(arguments, "--post-update");
        bool rollback = arguments.Contains("--update-rollback", StringComparer.OrdinalIgnoreCase);
        string? rollbackPlan = rollback
            ? ReadOption(arguments, "--update-rollback")
            : null;
        bool portable = arguments.Contains("--portable", StringComparer.OrdinalIgnoreCase);
        Current = new LauncherStartup(intent, postUpdatePlan, rollback, rollbackPlan, portable);
    }

    public static async Task PrepareProcessAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        string? waitValue = ReadOption(arguments, "--wait-for-pid");
        if (int.TryParse(
                waitValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int processId))
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
                // The parent already exited.
            }
        }

        if (OperatingSystem.IsWindows())
        {
            CleanupLegacyFiles(arguments);
        }
    }

    private static void CleanupLegacyFiles(IReadOnlyList<string> arguments)
    {
        string current = WindowsInstallationService.GetCurrentExecutable();
        var installation = new WindowsInstallationService(PlatformPaths.CreateDefault());
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    "--cleanup-legacy-file",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string candidate = Path.GetFullPath(arguments[++index]);
            if (installation.IsApprovedLegacyCleanupFile(candidate)
                && !WindowsInstallationService.PathsEqual(current, candidate)
                && File.Exists(candidate)
                && !PathSecurity.ContainsReparsePoint(candidate))
            {
                try
                {
                    File.Delete(candidate);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException)
                {
                    // A later repair/update can remove a file still held by antivirus.
                }
            }
        }
    }

    private static string? ReadOption(IReadOnlyList<string> arguments, string name)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
