using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SunshineAlley.Core;
using SunshineAlley.Platform.Installation;

namespace SunshineAlley.Platform.Update;

public sealed class LauncherUpdateService
{
    private const long MaximumPackageSize = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly LauncherApiClient _apiClient;
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;
    private readonly WindowsInstallationService _installation;
    private readonly UpdateManifestVerifier _manifestVerifier;
    private readonly LauncherIdentity _identity;

    public LauncherUpdateService(
        LauncherApiClient apiClient,
        HttpClient httpClient,
        ISettingsStore settingsStore,
        PlatformPaths paths,
        LauncherIdentity identity)
    {
        _apiClient = apiClient;
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _installation = new WindowsInstallationService(paths);
        _manifestVerifier = new UpdateManifestVerifier();
        _identity = identity;
    }

    public async Task<LauncherUpdateCheckResult> CheckAndLaunchAsync(
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new LauncherUpdateCheckResult(
                false,
                false,
                "Automatic application updates are not enabled on this operating system yet.");
        }

        InstallationInspection installation = _installation.Inspect();
        if (!installation.IsCurrentExecutable || installation.RegisteredExecutable is null)
        {
            return new LauncherUpdateCheckResult(
                false,
                false,
                "Update checks run only from the registered launcher installation.");
        }

        string currentVersion = GetCurrentVersion();
        string channel = await _settingsStore.GetAsync<string>(
            SettingsKeys.UpdateChannel,
            cancellationToken) ?? "stable";
        if (string.IsNullOrWhiteSpace(channel)
            || channel.Length > 32
            || !channel.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new LauncherException("The configured update channel is invalid.");
        }

        progress?.Report(new LauncherProgress("Checking for launcher updates…"));
        string executableHash = await ComputeSha256Async(
            installation.CurrentExecutable,
            cancellationToken);
        SignedLauncherUpdateEnvelope envelope = await _apiClient.GetLauncherUpdateAsync(
            new LauncherUpdateRequest
            {
                Version = currentVersion,
                RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                Channel = channel,
                ExecutableHash = executableHash,
                LayoutVersion = LauncherInstallationConstants.LayoutVersion,
                PublicKey = _identity.PublicKeyXml
            },
            cancellationToken);
        LauncherUpdateManifest manifest = _manifestVerifier.Verify(envelope).Manifest;
        ValidateManifest(manifest, currentVersion, channel);
        if (!manifest.UpdateAvailable)
        {
            return new LauncherUpdateCheckResult(
                false,
                false,
                "The launcher is up to date.",
                currentVersion);
        }

        long failedRelease = await _settingsStore.GetAsync<long?>(
            SettingsKeys.FailedLauncherReleaseId(
                manifest.Channel,
                manifest.RuntimeIdentifier),
            cancellationToken) ?? 0;
        if (manifest.ReleaseId <= failedRelease)
        {
            return new LauncherUpdateCheckResult(
                true,
                false,
                $"Launcher release {manifest.ReleaseId} previously failed startup and will not be retried. Publish a corrected build with a higher release ID.",
                manifest.Version);
        }

        long highestRelease = await _settingsStore.GetAsync<long?>(
            SettingsKeys.HighestLauncherReleaseId(
                manifest.Channel,
                manifest.RuntimeIdentifier),
            cancellationToken) ?? 0;
        if (manifest.ReleaseId <= highestRelease)
        {
            throw new LauncherException(
                $"Rejected replayed or downgraded release ID {manifest.ReleaseId}; this installation has already accepted {highestRelease}.");
        }

        LauncherUpdatePackage package = manifest.Package!;
        string applicationDirectory = Path.GetDirectoryName(
            installation.RegisteredExecutable)!;
        EnsureDiskSpace(applicationDirectory, package.Size);
        string transactionId = Guid.NewGuid().ToString("N");
        string stagingRoot = Path.Combine(
            applicationDirectory,
            ".update-staging",
            transactionId);
        Directory.CreateDirectory(stagingRoot);
        if (PathSecurity.ContainsReparsePoint(stagingRoot))
        {
            throw new LauncherException("The update staging directory traverses a link or junction.");
        }

        bool helperStarted = false;
        try
        {
            string stagedExecutable = Path.Combine(
                stagingRoot,
                LauncherInstallationConstants.ExecutableFileName + ".new");
            progress?.Report(new LauncherProgress($"Downloading launcher {manifest.Version}…"));
            await DownloadAndVerifyAsync(package, stagedExecutable, progress, cancellationToken);
            WindowsAuthenticodeVerifier.Verify(stagedExecutable);

            string backupExecutable = Path.Combine(stagingRoot, "previous.exe");
            string confirmationFile = Path.Combine(stagingRoot, "confirmed");
            string planPath = Path.Combine(stagingRoot, "update-plan.json");
            var plan = new LauncherUpdatePlan
            {
                ProductId = LauncherInstallationConstants.ProductId,
                TransactionId = transactionId,
                WaitForProcessId = Environment.ProcessId,
                InstalledExecutable = installation.RegisteredExecutable,
                StagedExecutable = stagedExecutable,
                BackupExecutable = backupExecutable,
                ConfirmationFile = confirmationFile,
                ExpectedSha256 = package.Sha256.ToLowerInvariant(),
                ExpectedPreviousSha256 = executableHash,
                Version = manifest.Version,
                ReleaseId = manifest.ReleaseId,
                Channel = manifest.Channel,
                RuntimeIdentifier = manifest.RuntimeIdentifier,
                RestartArguments = GetRestartArguments()
            };
            await WriteJsonAtomicAsync(planPath, plan, cancellationToken);
            StartTemporaryHelper(planPath, transactionId);
            helperStarted = true;
            return new LauncherUpdateCheckResult(
                true,
                true,
                $"Launcher {manifest.Version} is verified and will be installed now.",
                manifest.Version);
        }
        finally
        {
            if (!helperStarted)
            {
                try
                {
                    CleanupUnlaunchedTransaction(stagingRoot);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or LauncherException)
                {
                    // Preserve the download/signature error. A later repair can
                    // remove a staging file held by antivirus or another process.
                }
            }
        }
    }

    public async Task ConfirmPostUpdateAsync(
        string planPath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        LauncherUpdatePlan plan = await WindowsUpdateHelper.ReadAndValidatePlanAsync(
            planPath,
            requireStagedExecutable: false,
            cancellationToken);
        string current = WindowsInstallationService.GetCurrentExecutable();
        if (!WindowsInstallationService.PathsEqual(current, plan.InstalledExecutable))
        {
            throw new LauncherException("Post-update confirmation came from the wrong executable.");
        }

        string installedHash = await ComputeSha256Async(current, cancellationToken);
        if (!string.Equals(
                installedHash,
                plan.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException("The installed executable does not match the staged update hash.");
        }

        await _settingsStore.SetAsync(
            SettingsKeys.HighestLauncherReleaseId(plan.Channel, plan.RuntimeIdentifier),
            plan.ReleaseId,
            cancellationToken);
        await _settingsStore.SetAsync(
            SettingsKeys.FailedLauncherReleaseId(plan.Channel, plan.RuntimeIdentifier),
            0L,
            cancellationToken);
        await File.WriteAllTextAsync(
            plan.ConfirmationFile,
            $"{plan.ReleaseId}:{plan.Version}",
            new UTF8Encoding(false),
            cancellationToken);
    }

    public async Task RecordRolledBackUpdateAsync(
        string planPath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        LauncherUpdatePlan plan = await WindowsUpdateHelper.ReadAndValidatePlanAsync(
            planPath,
            requireStagedExecutable: false,
            cancellationToken);
        if (!WindowsInstallationService.PathsEqual(
                WindowsInstallationService.GetCurrentExecutable(),
                plan.InstalledExecutable))
        {
            throw new LauncherException(
                "A rolled-back update was reported by the wrong executable.");
        }

        string currentHash = await ComputeSha256Async(
            plan.InstalledExecutable,
            cancellationToken);
        if (string.Equals(
                currentHash,
                plan.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(
                "The update rollback marker was supplied, but the offered executable is still installed. Its rollback backup was preserved.");
        }

        await _settingsStore.SetAsync(
            SettingsKeys.FailedLauncherReleaseId(plan.Channel, plan.RuntimeIdentifier),
            plan.ReleaseId,
            cancellationToken);
        try
        {
            CleanupCompletedTransaction(plan);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or LauncherException)
        {
            // The failed release ID is already durable. A later repair can
            // remove a diagnostic file held open by antivirus.
        }
    }

    public async Task CleanupConfirmedUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        InstallationInspection installation = _installation.Inspect();
        if (!installation.IsCurrentExecutable || installation.RegisteredExecutable is null)
        {
            return;
        }

        string stagingRoot = Path.Combine(
            Path.GetDirectoryName(installation.RegisteredExecutable)!,
            ".update-staging");
        if (!Directory.Exists(stagingRoot) || PathSecurity.ContainsReparsePoint(stagingRoot))
        {
            return;
        }

        foreach (string transactionRoot in Directory.EnumerateDirectories(
            stagingRoot,
            "*",
            SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathSecurity.ContainsReparsePoint(transactionRoot)
                || !Guid.TryParseExact(Path.GetFileName(transactionRoot), "N", out _))
            {
                continue;
            }

            string confirmation = Path.Combine(transactionRoot, "confirmed");
            string planPath = Path.Combine(transactionRoot, "update-plan.json");
            if (!File.Exists(confirmation) || !File.Exists(planPath))
            {
                continue;
            }

            try
            {
                LauncherUpdatePlan plan = await WindowsUpdateHelper.ReadAndValidatePlanAsync(
                    planPath,
                    requireStagedExecutable: false,
                    cancellationToken);
                string currentHash = await ComputeSha256Async(
                    installation.RegisteredExecutable,
                    cancellationToken);
                if (!string.Equals(
                        currentHash,
                        plan.ExpectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CleanupCompletedTransaction(plan);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Cleanup must never prevent the launcher from starting. Invalid
                // or locked diagnostic transactions remain for repair/support.
            }
        }

        if (!Directory.EnumerateFileSystemEntries(stagingRoot).Any())
        {
            Directory.Delete(stagingRoot);
        }
    }

    private async Task DownloadAndVerifyAsync(
        LauncherUpdatePackage package,
        string destination,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        Uri url = ValidatePackageUrl(package.Url);
        string temporary = destination + ".download";
        using HttpResponseMessage response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        _ = ValidatePackageUrl(response.RequestMessage?.RequestUri?.AbsoluteUri ?? string.Empty);
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != package.Size)
        {
            throw new LauncherException(
                $"The update server declared {package.Size} bytes but returned {contentLength} bytes.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long completed = 0;
        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    completed += read;
                    if (completed > package.Size || completed > MaximumPackageSize)
                    {
                        throw new LauncherException("The downloaded update exceeded its signed size limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new LauncherProgress(
                        $"Downloading launcher update ({completed:N0} / {package.Size:N0} bytes)…",
                        completed,
                        package.Size));
                }

                await output.FlushAsync(cancellationToken);
            }

            if (completed != package.Size)
            {
                throw new LauncherException(
                    $"The downloaded update size was {completed} bytes; expected {package.Size}.");
            }

            string actualHash = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actualHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException("The downloaded update SHA-256 does not match the signed manifest.");
            }

            File.Move(temporary, destination, false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void ValidateManifest(
        LauncherUpdateManifest manifest,
        string currentVersion,
        string requestedChannel)
    {
        if (!string.Equals(
                manifest.RuntimeIdentifier,
                RuntimeInformation.RuntimeIdentifier,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.Channel, requestedChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException("The signed update manifest targets a different RID or channel.");
        }

        if (!manifest.UpdateAvailable)
        {
            return;
        }

        if (manifest.ReleaseId <= 0
            || manifest.Package is null
            || manifest.Package.Size <= 0
            || manifest.Package.Size > MaximumPackageSize
            || string.IsNullOrWhiteSpace(manifest.Package.Url)
            || string.IsNullOrWhiteSpace(manifest.Package.Sha256)
            || manifest.Package.Sha256.Length != 64
            || !manifest.Package.Sha256.All(Uri.IsHexDigit))
        {
            throw new LauncherException("The signed update manifest has invalid release or package fields.");
        }

        Version current = ParseVersion(currentVersion, "current launcher");
        Version offered = ParseVersion(manifest.Version, "offered update");
        if (offered <= current)
        {
            throw new LauncherException(
                $"The server offered non-newer launcher version {manifest.Version} over {currentVersion}.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinimumVersion)
            && current < ParseVersion(manifest.MinimumVersion, "minimum supported launcher"))
        {
            throw new LauncherException(
                $"This release requires launcher {manifest.MinimumVersion} or later. Install the current bridge release first.");
        }

        _ = ValidatePackageUrl(manifest.Package.Url);
    }

    private static Uri ValidatePackageUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                uri.Host,
                LauncherConstants.ServiceBaseUri.Host,
                StringComparison.OrdinalIgnoreCase)
            || uri.Port != LauncherConstants.ServiceBaseUri.Port
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new LauncherException("The update package URL is not an approved Sunshine Alley HTTPS URL.");
        }

        return uri;
    }

    private void StartTemporaryHelper(string planPath, string transactionId)
    {
        string helperRoot = Path.Combine(
            Path.GetTempPath(),
            "SunshineAlleyLauncher",
            "Updates",
            transactionId);
        Directory.CreateDirectory(helperRoot);
        string helper = Path.Combine(helperRoot, LauncherInstallationConstants.ExecutableFileName);
        File.Copy(WindowsInstallationService.GetCurrentExecutable(), helper, true);
        var start = new ProcessStartInfo
        {
            FileName = helper,
            WorkingDirectory = helperRoot,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--update-helper");
        start.ArgumentList.Add(planPath);
        _ = Process.Start(start)
            ?? throw new LauncherException("The temporary update helper could not be started.");
    }

    private static void EnsureDiskSpace(string applicationDirectory, long packageSize)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(applicationDirectory))
            ?? throw new LauncherException("The application drive could not be determined.");
        DriveInfo? drive = DriveInfo.GetDrives().FirstOrDefault(item =>
            string.Equals(item.Name, root, StringComparison.OrdinalIgnoreCase));
        if (drive is null || !drive.IsReady)
        {
            throw new LauncherException(
                "The application must be installed on an available local Windows drive before it can update.");
        }
        long currentSize = new FileInfo(WindowsInstallationService.GetCurrentExecutable()).Length;
        long required = checked(packageSize + currentSize + 64L * 1024 * 1024);
        if (drive.AvailableFreeSpace < required)
        {
            throw new LauncherException(
                $"The application drive needs at least {required:N0} free bytes to stage the update and rollback copy.");
        }
    }

    private static List<string> GetRestartArguments()
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
        string[] internalArgumentsWithValues =
        [
            "--update-helper",
            "--post-update",
            "--update-rollback",
            "--maintenance-helper",
            "--wait-for-pid",
            "--cleanup-legacy-file"
        ];
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var result = new List<string>();
        for (int index = 0; index < arguments.Length; index++)
        {
            if (internalArguments.Contains(arguments[index], StringComparer.OrdinalIgnoreCase))
            {
                if (internalArgumentsWithValues.Contains(
                        arguments[index],
                        StringComparer.OrdinalIgnoreCase)
                    && index + 1 < arguments.Length)
                {
                    index++;
                }

                continue;
            }

            result.Add(arguments[index]);
        }

        return result;
    }

    private static async Task<string> ComputeSha256Async(
        string file,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
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

    private static Version ParseVersion(string value, string description)
    {
        string numeric = value.Split('-', 2)[0].Split('+', 2)[0];
        return Version.TryParse(numeric, out Version? version)
            ? version
            : throw new LauncherException($"The {description} version '{value}' is invalid.");
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
        File.Move(temporary, path, true);
    }

    private static void CleanupUnlaunchedTransaction(string transactionRoot)
    {
        if (!Directory.Exists(transactionRoot)
            || PathSecurity.ContainsReparsePoint(transactionRoot))
        {
            return;
        }

        string[] knownNames =
        [
            LauncherInstallationConstants.ExecutableFileName + ".new",
            LauncherInstallationConstants.ExecutableFileName + ".new.download",
            "previous.exe",
            "confirmed",
            "update-plan.json",
            "update-plan.json.tmp"
        ];
        foreach (string name in knownNames)
        {
            DeleteRegularFile(Path.Combine(transactionRoot, name));
        }

        if (!Directory.EnumerateFileSystemEntries(transactionRoot).Any())
        {
            Directory.Delete(transactionRoot);
        }
    }

    private static void CleanupCompletedTransaction(LauncherUpdatePlan plan)
    {
        string transactionRoot = Path.GetDirectoryName(plan.ConfirmationFile)!;
        if (!Directory.Exists(transactionRoot)
            || PathSecurity.ContainsReparsePoint(transactionRoot))
        {
            return;
        }

        DeleteRegularFile(plan.BackupExecutable);
        DeleteRegularFile(plan.StagedExecutable);
        DeleteRegularFile(plan.StagedExecutable + ".download");
        DeleteRegularFile(plan.ConfirmationFile);
        DeleteRegularFile(Path.Combine(transactionRoot, "failed-update.exe"));
        DeleteRegularFile(Path.Combine(transactionRoot, "update-plan.json"));
        DeleteRegularFile(Path.Combine(transactionRoot, "update-plan.json.tmp"));
        if (!Directory.EnumerateFileSystemEntries(transactionRoot).Any())
        {
            Directory.Delete(transactionRoot);
        }
    }

    private static void DeleteRegularFile(string path)
    {
        if (File.Exists(path) && !PathSecurity.ContainsReparsePoint(path))
        {
            File.Delete(path);
        }
    }
}
