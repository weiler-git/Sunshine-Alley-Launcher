using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SunshineAlley.Core;
using SunshineAlley.Platform.Processes;

namespace SunshineAlley.Platform.Steam;

public sealed class SteamService : ISteamService
{
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private SteamInstallation? _cached;

    public async Task<SteamInstallation?> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        await _discoveryGate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            foreach ((string root, string? executable) in GetSteamCandidates())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string library in GetLibraries(root))
                {
                    string manifest = Path.Combine(
                        library,
                        "steamapps",
                        $"appmanifest_{LauncherConstants.SteamAppId}.acf");
                    if (!File.Exists(manifest))
                    {
                        continue;
                    }

                    string content = await File.ReadAllTextAsync(manifest, cancellationToken);
                    string? installDirectory = ReadVdfValue(content, "installdir");
                    if (string.IsNullOrWhiteSpace(installDirectory))
                    {
                        continue;
                    }

                    string gameDirectory = Path.Combine(
                        library,
                        "steamapps",
                        "common",
                        installDirectory);
                    int? buildId = int.TryParse(ReadVdfValue(content, "buildid"), out int parsed)
                        ? parsed
                        : null;
                    _cached = new SteamInstallation(
                        root,
                        executable ?? FindSteamExecutable(root),
                        gameDirectory,
                        buildId);
                    return _cached;
                }

                _cached = new SteamInstallation(root, executable ?? FindSteamExecutable(root), null, null);
            }

            return _cached;
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    public Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] names = OperatingSystem.IsMacOS()
            ? ["steam_osx", "Steam"]
            : ["steam", "steamwebhelper"];
        bool found = Process.GetProcesses().Any(process =>
        {
            using (process)
            {
                try
                {
                    return names.Any(name => string.Equals(
                        process.ProcessName,
                        name,
                        StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            }
        });
        return Task.FromResult(found);
    }

    public async Task EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await IsRunningAsync(cancellationToken))
        {
            return;
        }

        SteamInstallation? installation = await DiscoverAsync(cancellationToken);
        if (OperatingSystem.IsMacOS() && installation?.ExecutablePath is null)
        {
            StartDetached("/usr/bin/open", ["-a", "Steam"]);
        }
        else
        {
            (string executable, List<string> prefix) = ResolveSteamCommand(installation);
            StartDetached(executable, prefix);
        }

        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (await IsRunningAsync(cancellationToken))
            {
                return;
            }
        }

        throw new LauncherException("Steam did not become ready within 15 seconds.");
    }

    public async Task LaunchAppAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        await EnsureRunningAsync(cancellationToken);
        SteamInstallation? installation = await DiscoverAsync(cancellationToken);
        try
        {
            (string executable, List<string> allArguments) = ResolveSteamCommand(installation);
            allArguments.Add("-applaunch");
            allArguments.Add(
                LauncherConstants.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            allArguments.AddRange(arguments);
            StartDetached(executable, allArguments);
            return;
        }
        catch (LauncherException) when (OperatingSystem.IsMacOS())
        {
            string encodedArguments = string.Join(" ", arguments.Select(Uri.EscapeDataString));
            string uri = $"steam://run/{LauncherConstants.SteamAppId}//{encodedArguments}/";
            StartDetached("/usr/bin/open", [uri]);
            return;
        }
    }

    private static (string Executable, List<string> PrefixArguments) ResolveSteamCommand(
        SteamInstallation? installation)
    {
        if (OperatingSystem.IsLinux()
            && installation?.RootDirectory.Contains(
                Path.Combine(".var", "app", "com.valvesoftware.Steam"),
                StringComparison.Ordinal) == true
            && ProcessRunner.FindExecutable("flatpak") is string flatpak)
        {
            return (flatpak, ["run", "com.valvesoftware.Steam"]);
        }

        if (!string.IsNullOrWhiteSpace(installation?.ExecutablePath))
        {
            return (installation.ExecutablePath, []);
        }

        if (ProcessRunner.FindExecutable("steam") is string steam)
        {
            return (steam, []);
        }

        throw new LauncherException("Steam could not be found on this computer.");
    }

    public async Task<bool> HasLaunchOptionAsync(
        string marker,
        CancellationToken cancellationToken = default)
    {
        SteamInstallation? installation = await DiscoverAsync(cancellationToken);
        if (installation is null)
        {
            return false;
        }

        string userdata = Path.Combine(installation.RootDirectory, "userdata");
        if (!Directory.Exists(userdata))
        {
            return false;
        }

        foreach (string file in Directory.EnumerateFiles(
            userdata,
            "localconfig.vdf",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string content = await File.ReadAllTextAsync(file, cancellationToken);
                if (content.Contains(
                        LauncherConstants.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal)
                    && content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Steam can briefly replace this file while it is running.
            }
            catch (UnauthorizedAccessException)
            {
                // Another OS account's userdata is not relevant.
            }
        }

        return false;
    }

    public string GetRequiredLaunchOption(string scriptPath) => OperatingSystem.IsLinux()
        ? $"./{Path.GetFileName(scriptPath)} %command%"
        : $"\"{scriptPath}\" %command%";

    private static IEnumerable<(string Root, string? Executable)> GetSteamCandidates()
    {
        var candidates = new List<(string, string?)>();
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam", false);
            string? root = key?.GetValue("SteamPath") as string;
            string? executable = key?.GetValue("SteamExe") as string;
            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add((root, executable));
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                candidates.Add((Path.Combine(programFilesX86, "Steam"), null));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add((Path.Combine(user, "Library", "Application Support", "Steam"), null));
        }
        else
        {
            candidates.Add((Path.Combine(user, ".steam", "steam"), null));
            candidates.Add((Path.Combine(user, ".local", "share", "Steam"), null));
            candidates.Add((Path.Combine(
                user,
                ".var",
                "app",
                "com.valvesoftware.Steam",
                ".local",
                "share",
                "Steam"), null));
        }

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .Select(item => (Path.GetFullPath(item.Item1), item.Item2))
            .DistinctBy(item => item.Item1, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetLibraries(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        string file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file))
        {
            string content = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(
                content,
                "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string path = match.Groups["path"].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path))
                {
                    libraries.Add(path);
                }
            }
        }

        return libraries
            .Select(Path.GetFullPath)
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
    }

    private static string? FindSteamExecutable(string root)
    {
        IEnumerable<string> candidates;
        if (OperatingSystem.IsWindows())
        {
            candidates = [Path.Combine(root, "steam.exe")];
        }
        else if (OperatingSystem.IsMacOS())
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates =
            [
                "/Applications/Steam.app/Contents/MacOS/steam_osx",
                Path.Combine(user, "Applications", "Steam.app", "Contents", "MacOS", "steam_osx")
            ];
        }
        else
        {
            candidates =
            [
                Path.Combine(root, "steam.sh"),
                ProcessRunner.FindExecutable("steam") ?? string.Empty
            ];
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ReadVdfValue(string content, string name)
    {
        Match match = Regex.Match(
            content,
            $"\\\"{Regex.Escape(name)}\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static void StartDetached(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo)?.Dispose();
    }
}
