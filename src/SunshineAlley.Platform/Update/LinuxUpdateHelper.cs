using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SunshineAlley.Core;
using SunshineAlley.Platform.Installation;

namespace SunshineAlley.Platform.Update;

public static class LinuxUpdateHelper
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

        if (!OperatingSystem.IsLinux() || marker + 1 >= arguments.Count)
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
            string planPath = arguments[marker + 1];
            LauncherUpdatePlan plan = await ReadAndValidatePlanAsync(
                planPath,
                requireStagedExecutable: true,
                cancellationToken);
            activePlan = plan;
            ValidateHelperExecutable(plan.TransactionId);
            await WaitForExitAsync(plan.WaitForProcessId, cancellationToken);
            targetProcessExited = true;
            LinuxUpdateFileTransaction.Apply(plan);
            replacementApplied = true;
            updatedProcess = StartUpdatedLauncher(plan);
            bool confirmed = await WaitForConfirmationAsync(
                plan,
                updatedProcess,
                cancellationToken);
            if (confirmed)
            {
                confirmationReceived = true;
                CleanupCompletedTransaction(plan);
                return 0;
            }

            if (updatedProcess.HasExited)
            {
                LinuxUpdateFileTransaction.RollBack(plan);
                StartRollbackLauncher(plan);
                return 1;
            }

            // Preserve the backup and plan when startup takes longer than the
            // confirmation window. A later confirmed startup can clean them.
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
                        LinuxUpdateFileTransaction.RollBack(activePlan);
                    }
                    catch
                    {
                        // The failure log records the initiating error.
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
                    // The prior executable remains available for manual launch.
                }
            }

            return 1;
        }
        finally
        {
            updatedProcess?.Dispose();
        }
    }

    internal static async Task<LauncherUpdatePlan> ReadAndValidatePlanAsync(
        string planPath,
        bool requireStagedExecutable,
        CancellationToken cancellationToken)
    {
        PlatformPaths paths = PlatformPaths.CreateDefault();
        planPath = Path.GetFullPath(planPath);
        string updateRoot = paths.UpdateCacheDirectory;
        if (!PathSecurity.IsUnderRoot(updateRoot, planPath)
            || PathSecurity.ContainsReparsePointUnderRoot(updateRoot, planPath))
        {
            throw new LauncherException(
                "The Linux update plan is outside the protected XDG update directory.");
        }

        LauncherUpdatePlan plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(
            await File.ReadAllTextAsync(planPath, cancellationToken))
            ?? throw new LauncherException("The Linux update plan is empty.");
        if (!Guid.TryParseExact(plan.TransactionId, "N", out _))
        {
            throw new LauncherException("The Linux update transaction identity is invalid.");
        }

        string transactionRoot = Path.Combine(updateRoot, plan.TransactionId);
        string expectedPlan = Path.Combine(transactionRoot, "update-plan.json");
        string expectedStaged = Path.Combine(
            transactionRoot,
            LauncherInstallationConstants.LinuxExecutableFileName + ".new");
        string expectedBackup = Path.Combine(
            paths.ApplicationDirectory,
            $".{LauncherInstallationConstants.LinuxExecutableFileName}.{plan.TransactionId}.previous");
        string expectedConfirmation = Path.Combine(transactionRoot, "confirmed");
        if (!string.Equals(
                plan.ProductId,
                LauncherInstallationConstants.ProductId,
                StringComparison.Ordinal)
            || !LinuxInstallationService.PathsEqual(planPath, expectedPlan)
            || !LinuxInstallationService.PathsEqual(
                plan.InstalledExecutable,
                paths.LinuxExecutablePath)
            || !File.Exists(plan.InstalledExecutable)
            || PathSecurity.ContainsReparsePointUnderRoot(
                paths.ProductRoot,
                plan.InstalledExecutable)
            || !LinuxInstallationService.PathsEqual(
                plan.StagedExecutable,
                expectedStaged)
            || !LinuxInstallationService.PathsEqual(
                plan.BackupExecutable,
                expectedBackup)
            || !LinuxInstallationService.PathsEqual(
                plan.ConfirmationFile,
                expectedConfirmation)
            || PathSecurity.ContainsReparsePointUnderRoot(updateRoot, transactionRoot)
            || PathSecurity.ContainsReparsePointUnderRoot(
                updateRoot,
                plan.ConfirmationFile)
            || PathSecurity.ContainsReparsePointUnderRoot(
                paths.ProductRoot,
                plan.BackupExecutable)
            || plan.ExpectedSha256.Length != 64
            || !plan.ExpectedSha256.All(Uri.IsHexDigit)
            || plan.ExpectedPreviousSha256.Length != 64
            || !plan.ExpectedPreviousSha256.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(plan.Channel)
            || plan.Channel.Length > 32
            || !plan.Channel.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            || !string.Equals(
                plan.RuntimeIdentifier,
                RuntimeInformation.RuntimeIdentifier,
                StringComparison.OrdinalIgnoreCase)
            || !plan.RuntimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(plan.Version)
            || plan.WaitForProcessId <= 0
            || ContainsInternalRestartArgument(plan.RestartArguments)
            || plan.ReleaseId <= 0
            || !LinuxInstallationService.IsInstallationMarkerValid(
                paths.InstallationMarkerFile))
        {
            throw new LauncherException(
                "The Linux update plan contains an unsafe or invalid target.");
        }

        if (requireStagedExecutable)
        {
            if (!File.Exists(plan.StagedExecutable)
                || (File.GetAttributes(plan.StagedExecutable) & FileAttributes.ReparsePoint) != 0)
            {
                throw new LauncherException("The staged Linux update executable is missing or linked.");
            }

            string hash = await ComputeSha256Async(
                plan.StagedExecutable,
                cancellationToken);
            if (!string.Equals(hash, plan.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException(
                    "The staged Linux executable changed after signed-manifest verification.");
            }

            EnsureExecutable(plan.StagedExecutable);
        }

        return plan;
    }

    internal static void CleanupCompletedTransaction(LauncherUpdatePlan plan)
    {
        string transactionRoot = Path.GetDirectoryName(plan.ConfirmationFile)!;
        if (!Directory.Exists(transactionRoot)
            || (File.GetAttributes(transactionRoot) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        DeleteRegularFile(plan.BackupExecutable);
        DeleteRegularFile(plan.StagedExecutable);
        DeleteRegularFile(plan.StagedExecutable + ".download");
        DeleteRegularFile(plan.ConfirmationFile);
        DeleteRegularFile(Path.Combine(transactionRoot, "update-plan.json"));
        DeleteRegularFile(Path.Combine(transactionRoot, "update-plan.json.tmp"));
        DeleteRegularFile(Path.Combine(transactionRoot, "update-helper"));
        if (!Directory.EnumerateFileSystemEntries(transactionRoot).Any())
        {
            Directory.Delete(transactionRoot);
            string updateRoot = Path.GetDirectoryName(transactionRoot)!;
            if (Directory.Exists(updateRoot)
                && !Directory.EnumerateFileSystemEntries(updateRoot).Any())
            {
                Directory.Delete(updateRoot);
            }
        }
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
            ?? throw new LauncherException("The updated Linux launcher could not be started.");
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
            ?? throw new LauncherException("The rolled-back Linux launcher could not be restarted.");
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

    private static void ValidateHelperExecutable(string transactionId)
    {
        PlatformPaths paths = PlatformPaths.CreateDefault();
        string expected = Path.Combine(
            paths.UpdateCacheDirectory,
            transactionId,
            "update-helper");
        string current = LinuxInstallationService.GetCurrentExecutable();
        if (!LinuxInstallationService.PathsEqual(current, expected)
            || PathSecurity.ContainsReparsePointUnderRoot(
                paths.UpdateCacheDirectory,
                current))
        {
            throw new LauncherException(
                "Linux update helper mode can run only from its protected XDG transaction directory.");
        }
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
            // The old launcher already exited.
        }
    }

    private static void EnsureExecutable(string path)
    {
        UnixFileMode mode = File.GetUnixFileMode(path);
        if ((mode & UnixFileMode.UserExecute) == 0)
        {
            throw new LauncherException(
                "The staged Linux launcher lost its executable permission.");
        }
    }

    private static void DeleteRegularFile(string path)
    {
        if (File.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
        {
            File.Delete(path);
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
                Path.Combine(
                    paths.LogDirectory,
                    $"update-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log"),
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

internal static class LinuxUpdateFileTransaction
{
    public static void Apply(LauncherUpdatePlan plan)
    {
        VerifyHash(
            plan.InstalledExecutable,
            plan.ExpectedPreviousSha256,
            "The installed Linux launcher changed after the update check.");
        if (File.Exists(plan.BackupExecutable))
        {
            throw new LauncherException(
                "The Linux update rollback path already exists; the installation was not changed.");
        }

        string localReplacement = GetLocalReplacement(plan);
        bool replaced = false;
        try
        {
            File.Copy(plan.InstalledExecutable, plan.BackupExecutable, false);
            LinuxInstallationService.SetExecutableMode(plan.BackupExecutable);
            VerifyHash(
                plan.BackupExecutable,
                plan.ExpectedPreviousSha256,
                "The Linux rollback copy failed verification.");

            File.Copy(plan.StagedExecutable, localReplacement, false);
            LinuxInstallationService.SetExecutableMode(localReplacement);
            VerifyHash(
                localReplacement,
                plan.ExpectedSha256,
                "The Linux update changed while being copied to the application filesystem.");

            // localReplacement and InstalledExecutable share a directory, so the
            // final rename is atomic even when XDG_CACHE_HOME is another filesystem.
            File.Move(localReplacement, plan.InstalledExecutable, true);
            replaced = true;
            VerifyHash(
                plan.InstalledExecutable,
                plan.ExpectedSha256,
                "The installed Linux update failed post-replacement verification.");
        }
        catch
        {
            if (replaced && File.Exists(plan.BackupExecutable))
            {
                RollBack(plan);
            }
            else if (File.Exists(plan.BackupExecutable))
            {
                VerifyHash(
                    plan.BackupExecutable,
                    plan.ExpectedPreviousSha256,
                    "The unused Linux rollback copy failed verification.");
                File.Delete(plan.BackupExecutable);
            }

            throw;
        }
        finally
        {
            if (File.Exists(localReplacement))
            {
                File.Delete(localReplacement);
            }
        }
    }

    public static void RollBack(LauncherUpdatePlan plan)
    {
        if (!File.Exists(plan.BackupExecutable))
        {
            throw new LauncherException(
                "The Linux update failed and its rollback executable is missing.");
        }

        VerifyHash(
            plan.BackupExecutable,
            plan.ExpectedPreviousSha256,
            "The Linux rollback executable does not match the launcher that began the update.");
        string localReplacement = GetLocalReplacement(plan) + ".rollback";
        try
        {
            File.Copy(plan.BackupExecutable, localReplacement, false);
            LinuxInstallationService.SetExecutableMode(localReplacement);
            VerifyHash(
                localReplacement,
                plan.ExpectedPreviousSha256,
                "The Linux rollback copy changed before replacement.");
            File.Move(localReplacement, plan.InstalledExecutable, true);
            VerifyHash(
                plan.InstalledExecutable,
                plan.ExpectedPreviousSha256,
                "The restored Linux launcher failed verification.");
        }
        finally
        {
            if (File.Exists(localReplacement))
            {
                File.Delete(localReplacement);
            }
        }
    }

    private static string GetLocalReplacement(LauncherUpdatePlan plan) => Path.Combine(
        Path.GetDirectoryName(plan.InstalledExecutable)!,
        $".{LauncherInstallationConstants.LinuxExecutableFileName}.{plan.TransactionId}.new");

    private static void VerifyHash(string file, string expectedHash, string message)
    {
        using FileStream stream = File.OpenRead(file);
        string actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(message);
        }
    }
}
