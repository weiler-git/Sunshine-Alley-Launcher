using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SunshineAlley.Core;

public sealed class ModPackService
{
    private readonly LauncherApiClient _apiClient;
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;
    private readonly Uri _serviceBaseUri;
    private readonly ConcurrentDictionary<int, ModPackState> _states = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _packGates = new();

    public ModPackService(
        LauncherApiClient apiClient,
        HttpClient httpClient,
        ISettingsStore settingsStore,
        Uri? serviceBaseUri = null)
    {
        _apiClient = apiClient;
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _serviceBaseUri = serviceBaseUri ?? LauncherConstants.ServiceBaseUri;
    }

    public ModPackState GetState(int modPackId) =>
        _states.GetOrAdd(modPackId, static id => new ModPackState(id));

    public async Task<ModPackState> EnsureVerifiedAsync(
        int modPackId,
        string modDataDirectory,
        bool isAdmin,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (modPackId <= 0)
        {
            return GetState(modPackId);
        }

        ModPackState state = GetState(modPackId);
        SemaphoreSlim gate = _packGates.GetOrAdd(modPackId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            state.SetVerified(false);
            string packRoot = GetPackRoot(modDataDirectory, modPackId);

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                List<FileHashDto> files = await HashFilesAsync(packRoot, progress, cancellationToken);
                var request = new VerifyModPackRequest
                {
                    IsAdmin = isAdmin,
                    ModPack = modPackId,
                    RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                    Files = files
                };

                progress?.Report(new LauncherProgress("Checking files with the server…"));
                FileValidationResponse response = await _apiClient.VerifyFilesAsync(request, cancellationToken);
                bool changed = await ApplyResponseAsync(
                    response,
                    modDataDirectory,
                    packRoot,
                    state,
                    progress,
                    cancellationToken);

                if (!changed)
                {
                    await InstallDefaultConfigsAsync(packRoot, cancellationToken);
                    state.SetVerified(true);
                    progress?.Report(new LauncherProgress("File scanning complete."));
                    return state;
                }

                progress?.Report(new LauncherProgress($"Rechecking synchronized files ({attempt}/4)…"));
            }

            throw new LauncherException(
                $"Modpack {modPackId} did not reach a verified state after four synchronization passes.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetOptionalEnabledAsync(
        int modPackId,
        string name,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ModPackState state = GetState(modPackId);
        if (!state.SetOptionalEnabled(name, enabled))
        {
            throw new LauncherException($"Optional mod '{name}' is not known for modpack {modPackId}.");
        }

        await _settingsStore.SetAsync(
            SettingsKeys.OptionalEnabled(modPackId, name),
            enabled,
            cancellationToken);
    }

    public static string GetPackRoot(string modDataDirectory, int modPackId) =>
        Path.Combine(
            Path.GetFullPath(modDataDirectory),
            LauncherConstants.ModPacksDirectoryName,
            modPackId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static async Task<string> ComputeFileHashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<List<FileHashDto>> HashFilesAsync(
        string packRoot,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(packRoot))
        {
            return [];
        }

        string[] files = Directory
            .EnumerateFiles(packRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var result = new List<FileHashDto>(files.Length);
        for (int index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = files[index];
            progress?.Report(new LauncherProgress(
                $"Scanning {Path.GetFileName(file)}",
                index + 1,
                files.Length));
            result.Add(new FileHashDto
            {
                FilePath = file,
                Hash = await ComputeFileHashAsync(file, cancellationToken),
                Extenstion = Path.GetExtension(file)
            });
        }

        return result;
    }

    private async Task<bool> ApplyResponseAsync(
        FileValidationResponse response,
        string modDataDirectory,
        string packRoot,
        ModPackState state,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool changed = false;
        List<IllegalFileDto> illegalFiles = response.Illegal ?? [];
        List<ServerFileDto> missingFiles = response.Missing ?? [];
        List<ServerFileDto> optionalFiles = response.Optional ?? [];

        foreach (IllegalFileDto illegal in illegalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PathSecurity.IsUnderRoot(packRoot, illegal.File) || !File.Exists(illegal.File))
            {
                continue;
            }

            File.Delete(illegal.File);
            changed = true;
        }

        foreach (ServerFileDto missing in missingFiles)
        {
            await DownloadServerFileAsync(
                missing.DownloadUrl,
                modDataDirectory,
                packRoot,
                progress,
                cancellationToken);
            changed = true;
        }

        var optionalStates = new Dictionary<string, OptionalModState>(StringComparer.OrdinalIgnoreCase);
        foreach (ServerFileDto file in optionalFiles)
        {
            string name = NormalizeOptionalModName(file.ModName);
            if (name.Length == 0)
            {
                continue;
            }

            bool enabled = await _settingsStore.GetAsync<bool?>(
                SettingsKeys.OptionalEnabled(state.Id, name),
                cancellationToken) ?? false;
            optionalStates[name] = new OptionalModState(name, enabled);
            await _settingsStore.SetAsync(
                SettingsKeys.OptionalKnown(state.Id, name),
                true,
                cancellationToken);
        }

        state.ReplaceOptionalMods(optionalStates.Values);

        foreach (ServerFileDto optional in optionalFiles)
        {
            string name = NormalizeOptionalModName(optional.ModName);
            if (!optionalStates.TryGetValue(name, out OptionalModState? option))
            {
                continue;
            }

            string target = ResolveDownloadTarget(
                optional.DownloadUrl,
                modDataDirectory,
                packRoot);
            if (option.Enabled && !optional.Ok)
            {
                await DownloadServerFileAsync(
                    optional.DownloadUrl,
                    modDataDirectory,
                    packRoot,
                    progress,
                    cancellationToken);
                changed = true;
            }
            else if (!option.Enabled && optional.Ok && File.Exists(target))
            {
                File.Delete(target);
                changed = true;
            }
        }

        return changed;
    }

    private async Task DownloadServerFileAsync(
        string downloadUrl,
        string modDataDirectory,
        string packRoot,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        string destination = ResolveDownloadTarget(downloadUrl, modDataDirectory, packRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".download";

        using HttpResponseMessage response = await _httpClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? 0;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[128 * 1024];
        long completed = 0;
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                completed += read;
                progress?.Report(new LauncherProgress(
                    $"Downloading {Path.GetFileName(destination)}",
                    completed,
                    total));
            }

            await output.FlushAsync(cancellationToken);
            output.Close();
            File.Move(temporary, destination, true);
        }
        catch
        {
            output.Close();
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    private string ResolveDownloadTarget(
        string downloadUrl,
        string modDataDirectory,
        string packRoot)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, _serviceBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != _serviceBaseUri.Port)
        {
            throw new LauncherException($"Rejected untrusted mod download URL '{downloadUrl}'.");
        }

        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length < 3
            || !string.Equals(
                segments[0],
                LauncherConstants.ModPacksDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            || segments.Any(segment =>
                segment is "." or ".."
                || segment.Contains(Path.DirectorySeparatorChar)
                || segment.Contains(Path.AltDirectorySeparatorChar)))
        {
            throw new LauncherException($"Rejected unsafe mod download path '{uri.AbsolutePath}'.");
        }

        segments[0] = LauncherConstants.ModPacksDirectoryName;
        string destination = segments.Aggregate(
            Path.GetFullPath(modDataDirectory),
            Path.Combine);
        if (!PathSecurity.IsUnderRoot(packRoot, destination))
        {
            throw new LauncherException(
                $"Rejected mod download outside modpack {Path.GetFileName(packRoot)}.");
        }

        return destination;
    }

    private static async Task InstallDefaultConfigsAsync(
        string packRoot,
        CancellationToken cancellationToken)
    {
        string? essentials = FileSystemCase.FindDirectory(
            packRoot,
            LauncherConstants.GameEssentialsDirectoryName);
        if (essentials is null)
        {
            return;
        }

        string? defaults = FileSystemCase.FindDirectory(essentials, "defaultconfig");
        string? bepinex = FileSystemCase.FindDirectory(packRoot, "BepInEx")
            ?? FileSystemCase.FindDirectory(packRoot, "bepinex");
        if (defaults is null || bepinex is null)
        {
            return;
        }

        string destinationDirectory = Path.Combine(bepinex, "config");
        Directory.CreateDirectory(destinationDirectory);
        foreach (string source in Directory.EnumerateFiles(defaults, "*.txt"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(source);
            string destination = Path.Combine(destinationDirectory, name);
            if (!File.Exists(destination))
            {
                File.Copy(source, destination);
            }
        }

        await Task.CompletedTask;
    }

    private static string NormalizeOptionalModName(string input)
    {
        string normalized = Regex.Replace(input.Trim(), "[^A-Za-z0-9-]+", "-");
        return Regex.Replace(normalized, "-+", "-").Trim('-');
    }
}

public static class PathSecurity
{
    public static bool IsUnderRoot(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.GetFullPath(candidate);
        string relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);
        if (relative == ".")
        {
            return true;
        }

        string firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            2,
            StringSplitOptions.RemoveEmptyEntries)[0];
        return !Path.IsPathRooted(relative)
            && !string.Equals(firstSegment, "..", StringComparison.Ordinal);
    }
}

public static class FileSystemCase
{
    public static string? FindDirectory(string parent, string name)
    {
        if (!Directory.Exists(parent))
        {
            return null;
        }

        string exact = Path.Combine(parent, name);
        if (Directory.Exists(exact))
        {
            return exact;
        }

        return Directory.EnumerateDirectories(parent)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindFile(string parent, string name)
    {
        if (!Directory.Exists(parent))
        {
            return null;
        }

        string exact = Path.Combine(parent, name);
        if (File.Exists(exact))
        {
            return exact;
        }

        return Directory.EnumerateFiles(parent)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                name,
                StringComparison.OrdinalIgnoreCase));
    }
}
