using System.Diagnostics;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Launch;

internal static class GameProcessWatcher
{
    public static async Task EnsureNotRunningAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        using Process? existing = Find(executable);
        if (existing is not null && !existing.HasExited)
        {
            throw new LauncherException(
                $"Valheim is already running (process {existing.Id}). Close it before launching another profile.");
        }

        await Task.CompletedTask;
    }

    public static async Task<int> WaitForSteamGameAndExitAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process? process = Find(executable);
            if (process is not null)
            {
                using (process)
                {
                    int id = process.Id;
                    await process.WaitForExitAsync(cancellationToken);
                    return id;
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new LauncherException("Steam did not start Valheim within 45 seconds.");
    }

    private static Process? Find(string executable)
    {
        string expected = Path.GetFullPath(executable);
        string expectedName = Path.GetFileNameWithoutExtension(executable);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? actual = process.MainModule?.FileName;
                if (actual is not null
                    && string.Equals(Path.GetFullPath(actual), expected, comparison))
                {
                    return process;
                }

                if (string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase)
                    || (OperatingSystem.IsLinux()
                        && process.ProcessName.StartsWith("valheim", StringComparison.OrdinalIgnoreCase)))
                {
                    return process;
                }
            }
            catch
            {
                // Processes belonging to another user can deny module inspection.
            }

            process.Dispose();
        }

        return null;
    }
}
