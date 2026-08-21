using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using SunshineAlley.Core;
using SunshineAlley.Platform.Installation;

namespace SunshineAlley.Platform.Update;

public static class WindowsUpdateHelper
{
    public static async Task<int?> TryRunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        int marker = IndexOf(arguments, "--update-helper");
        if (marker < 0)
        {
            return null;
        }

        if (!OperatingSystem.IsWindows() || marker + 1 >= arguments.Count)
        {
            return 2;
        }

        LauncherUpdatePlan? activePlan = null;
        Process? updatedProcess = null;
        bool replacementApplied = false;
        bool targetProcessExited = false;
        bool confirmationReceived = false;
        try
        {
            LauncherUpdatePlan plan = await ReadAndValidatePlanAsync(
                arguments[marker + 1],
                requireStagedExecutable: true,
                cancellationToken);
            activePlan = plan;
            ValidateHelperExecutable(plan.TransactionId);
            await WaitForExitAsync(plan.WaitForProcessId, cancellationToken);
            targetProcessExited = true;
            Apply(plan);
            replacementApplied = true;
            updatedProcess = StartUpdatedLauncher(plan);
            bool confirmed = await WaitForConfirmationAsync(
                plan,
                updatedProcess,
                cancellationToken);
            if (confirmed)
            {
                confirmationReceived = true;
                CleanupSuccessfulUpdate(plan);
                return 0;
            }

            if (updatedProcess.HasExited)
            {
                RollBack(plan);
                StartRollbackLauncher(plan);
                return 1;
            }

            // Do not terminate a responsive-looking launcher merely because its
            // first-run initialization exceeded the confirmation window. The
            // backup and plan remain available for diagnosis/manual rollback.
            return 3;
        }
        catch (Exception exception)
        {
            await WriteFailureLogAsync(exception);
            bool updatedStillRunning = updatedProcess is not null
                && !HasExitedSafely(updatedProcess);
            if (activePlan is not null
                && targetProcessExited
                && !confirmationReceived
                && !updatedStillRunning)
            {
                if (replacementApplied && File.Exists(activePlan.BackupExecutable))
                {
                    try
                    {
                        RollBack(activePlan);
                    }
                    catch
                    {
                        // The failure log already records the initiating error.
                    }
                }

                try
                {
                    if (File.Exists(activePlan.InstalledExecutable))
                    {
                        StartRollbackLauncher(activePlan);
                    }
                }
                catch
                {
                    // The log contains the original error; the unchanged target
                    // can still be launched normally by the player.
                }
            }

            return 1;
        }
        finally
        {
            updatedProcess?.Dispose();
        }
    }

    public static async Task<LauncherUpdatePlan> ReadAndValidatePlanAsync(
        string planPath,
        bool requireStagedExecutable,
        CancellationToken cancellationToken)
    {
        planPath = Path.GetFullPath(planPath);
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            LauncherInstallationConstants.InstallRegistryPath,
            false);
        string? registered = key?.GetValue("ExecutablePath") as string;
        if (registered is null)
        {
            throw new LauncherException("The update target is not registered.");
        }

        string appDirectory = Path.GetDirectoryName(registered)!;
        string stagingRoot = Path.Combine(appDirectory, ".update-staging");
        if (!PathSecurity.IsUnderRoot(stagingRoot, planPath)
            || PathSecurity.ContainsReparsePoint(planPath))
        {
            throw new LauncherException("The update plan is outside the registered staging directory.");
        }

        LauncherUpdatePlan plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(
            await File.ReadAllTextAsync(planPath, cancellationToken))
            ?? throw new LauncherException("The update plan is empty.");
        if (!Guid.TryParseExact(plan.TransactionId, "N", out _))
        {
            throw new LauncherException("The update transaction identity is invalid.");
        }

        string transactionRoot = Path.Combine(stagingRoot, plan.TransactionId);
        string expectedPlan = Path.Combine(transactionRoot, "update-plan.json");
        string expectedStaged = Path.Combine(
            transactionRoot,
            LauncherInstallationConstants.ExecutableFileName + ".new");
        string expectedBackup = Path.Combine(transactionRoot, "previous.exe");
        string expectedConfirmation = Path.Combine(transactionRoot, "confirmed");
        if (!string.Equals(
                plan.ProductId,
                LauncherInstallationConstants.ProductId,
                StringComparison.Ordinal)
            || !WindowsInstallationService.PathsEqual(planPath, expectedPlan)
            || !WindowsInstallationService.PathsEqual(registered, plan.InstalledExecutable)
            || !File.Exists(registered)
            || PathSecurity.ContainsReparsePoint(registered)
            || !WindowsInstallationService.PathsEqual(plan.StagedExecutable, expectedStaged)
            || !WindowsInstallationService.PathsEqual(plan.BackupExecutable, expectedBackup)
            || !WindowsInstallationService.PathsEqual(plan.ConfirmationFile, expectedConfirmation)
            || PathSecurity.ContainsReparsePoint(transactionRoot)
            || PathSecurity.ContainsReparsePoint(plan.BackupExecutable)
            || PathSecurity.ContainsReparsePoint(plan.ConfirmationFile)
            || plan.ExpectedSha256.Length != 64
            || !plan.ExpectedSha256.All(Uri.IsHexDigit)
            || plan.ExpectedPreviousSha256.Length != 64
            || !plan.ExpectedPreviousSha256.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(plan.Channel)
            || plan.Channel.Length > 32
            || !plan.Channel.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            || !string.Equals(plan.RuntimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(plan.Version)
            || plan.WaitForProcessId <= 0
            || ContainsInternalRestartArgument(plan.RestartArguments)
            || plan.ReleaseId <= 0)
        {
            throw new LauncherException("The update plan contains an unsafe or invalid target.");
        }

        if (requireStagedExecutable)
        {
            if (!File.Exists(plan.StagedExecutable)
                || PathSecurity.ContainsReparsePoint(plan.StagedExecutable))
            {
                throw new LauncherException("The staged update executable is missing or linked.");
            }

            string hash = await ComputeSha256Async(plan.StagedExecutable, cancellationToken);
            if (!string.Equals(hash, plan.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException("The staged executable changed after verification.");
            }

            // Re-evaluate the Windows trust chain and exact publisher in helper
            // mode immediately before replacement, not only after download.
            WindowsAuthenticodeVerifier.Verify(plan.StagedExecutable);
        }

        return plan;
    }

    private static void Apply(LauncherUpdatePlan plan)
    {
        VerifyHash(
            plan.InstalledExecutable,
            plan.ExpectedPreviousSha256,
            "The installed launcher changed after the update check.");
        if (File.Exists(plan.BackupExecutable))
        {
            File.Delete(plan.BackupExecutable);
        }

        // Staging is deliberately beneath App so this same-volume replacement
        // atomically creates the rollback copy without a missing-target window.
        File.Replace(
            plan.StagedExecutable,
            plan.InstalledExecutable,
            plan.BackupExecutable,
            ignoreMetadataErrors: true);
    }

    private static Process StartUpdatedLauncher(LauncherUpdatePlan plan)
    {
        var start = new ProcessStartInfo
        {
            FileName = plan.InstalledExecutable,
            WorkingDirectory = Path.GetDirectoryName(plan.InstalledExecutable)!,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--post-update");
        start.ArgumentList.Add(Path.Combine(
            Path.GetDirectoryName(plan.ConfirmationFile)!,
            "update-plan.json"));
        foreach (string argument in plan.RestartArguments)
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start)
            ?? throw new LauncherException("The updated launcher could not be started.");
    }

    private static async Task<bool> WaitForConfirmationAsync(
        LauncherUpdatePlan plan,
        Process process,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(plan.ConfirmationFile))
            {
                return true;
            }

            if (process.HasExited)
            {
                return false;
            }

            await Task.Delay(500, cancellationToken);
        }

        return File.Exists(plan.ConfirmationFile);
    }

    private static void CleanupSuccessfulUpdate(LauncherUpdatePlan plan)
    {
        string transactionRoot = Path.GetDirectoryName(plan.ConfirmationFile)!;
        if (Directory.Exists(transactionRoot)
            && !PathSecurity.ContainsReparsePoint(transactionRoot))
        {
            DeleteRegularFile(plan.BackupExecutable);
            DeleteRegularFile(plan.StagedExecutable);
            DeleteRegularFile(plan.StagedExecutable + ".download");
            DeleteRegularFile(plan.ConfirmationFile);
            DeleteRegularFile(Path.Combine(transactionRoot, "update-plan.json"));
            DeleteRegularFile(Path.Combine(transactionRoot, "update-plan.json.tmp"));
            if (!Directory.EnumerateFileSystemEntries(transactionRoot).Any())
            {
                Directory.Delete(transactionRoot);
                string stagingRoot = Path.GetDirectoryName(transactionRoot)!;
                if (Directory.Exists(stagingRoot)
                    && !Directory.EnumerateFileSystemEntries(stagingRoot).Any())
                {
                    Directory.Delete(stagingRoot);
                }
            }
        }
    }

    private static void RollBack(LauncherUpdatePlan plan)
    {
        if (!File.Exists(plan.BackupExecutable))
        {
            throw new LauncherException("The update failed and its rollback executable is missing.");
        }

        VerifyHash(
            plan.BackupExecutable,
            plan.ExpectedPreviousSha256,
            "The rollback executable does not match the launcher that began the update.");

        string failed = Path.Combine(
            Path.GetDirectoryName(plan.ConfirmationFile)!,
            "failed-update.exe");
        if (File.Exists(failed))
        {
            File.Delete(failed);
        }

        File.Replace(
            plan.BackupExecutable,
            plan.InstalledExecutable,
            failed,
            ignoreMetadataErrors: true);
    }

    private static void StartRollbackLauncher(LauncherUpdatePlan plan)
    {
        var start = new ProcessStartInfo
        {
            FileName = plan.InstalledExecutable,
            WorkingDirectory = Path.GetDirectoryName(plan.InstalledExecutable)!,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--update-rollback");
        start.ArgumentList.Add(Path.Combine(
            Path.GetDirectoryName(plan.ConfirmationFile)!,
            "update-plan.json"));
        foreach (string argument in plan.RestartArguments)
        {
            start.ArgumentList.Add(argument);
        }
        _ = Process.Start(start)
            ?? throw new LauncherException("The rolled-back launcher could not be restarted.");
    }

    private static bool ContainsInternalRestartArgument(IEnumerable<string> arguments)
    {
        string[] internalArguments =
        [
            "--update-helper",
            "--post-update",
            "--update-rollback",
            "--maintenance-helper",
            "--wait-for-pid",
            "--cleanup-legacy-file"
        ];
        return arguments.Any(argument => internalArguments.Contains(
            argument,
            StringComparer.OrdinalIgnoreCase));
    }

    private static void ValidateHelperExecutable(string transactionId)
    {
        string expected = Path.Combine(
            Path.GetTempPath(),
            "SunshineAlleyLauncher",
            "Updates",
            transactionId,
            LauncherInstallationConstants.ExecutableFileName);
        string current = WindowsInstallationService.GetCurrentExecutable();
        if (!WindowsInstallationService.PathsEqual(current, expected)
            || PathSecurity.ContainsReparsePoint(current))
        {
            throw new LauncherException(
                "Update helper mode can run only from its protected temporary transaction directory.");
        }
    }

    private static bool HasExitedSafely(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void DeleteRegularFile(string path)
    {
        if (File.Exists(path) && !PathSecurity.ContainsReparsePoint(path))
        {
            File.Delete(path);
        }
    }

    private static void VerifyHash(string file, string expectedHash, string message)
    {
        using FileStream stream = File.OpenRead(file);
        string actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(message);
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

    private static async Task<string> ComputeSha256Async(
        string file,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task WriteFailureLogAsync(Exception exception)
    {
        try
        {
            PlatformPaths paths = PlatformPaths.CreateDefault();
            Directory.CreateDirectory(paths.LogDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(paths.LogDirectory, $"update-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log"),
                exception.ToString());
        }
        catch
        {
            // Preserve the original helper exit code if logging is unavailable.
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
