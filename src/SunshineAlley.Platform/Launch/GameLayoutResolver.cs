using System.Runtime.InteropServices;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Launch;

internal static class GameLayoutResolver
{
    public static string PlatformRid => (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.OSArchitecture) switch
    {
        (true, _, Architecture.X64) => "win-x64",
        (true, _, Architecture.Arm64) => "win-arm64",
        (_, true, Architecture.X64) => "linux-x64",
        (_, true, Architecture.Arm64) => "linux-arm64",
        (_, _, Architecture.X64) when OperatingSystem.IsMacOS() => "osx-x64",
        (_, _, Architecture.Arm64) when OperatingSystem.IsMacOS() => "osx-arm64",
        _ => RuntimeInformation.RuntimeIdentifier
    };

    public static string? FindGameExecutable(string gameDirectory)
    {
        if (!Directory.Exists(gameDirectory))
        {
            return null;
        }

        string[] candidates = OperatingSystem.IsWindows()
            ? ["valheim.exe"]
            : OperatingSystem.IsMacOS()
                ?
                [
                    Path.Combine("valheim.app", "Contents", "MacOS", "Valheim"),
                    Path.Combine("Valheim.app", "Contents", "MacOS", "Valheim")
                ]
                : ["valheim.x86_64", "valheim.x64", "valheim"];

        return candidates
            .Select(relative => ResolveRelativeCaseInsensitive(gameDirectory, relative))
            .FirstOrDefault(path => path is not null && File.Exists(path));
    }

    public static string? FindPreloader(string packRoot)
    {
        string[] candidates =
        [
            Path.Combine("BepInEx", "core", "BepInEx.Preloader.dll"),
            Path.Combine("bepinex", "core", "BepInEx.Preloader.dll"),
            Path.Combine("BepInEx", "core", "BepInEx.Unity.Mono.Preloader.dll"),
            Path.Combine("bepinex", "core", "BepInEx.Unity.Mono.Preloader.dll")
        ];
        return candidates
            .Select(relative => ResolveRelativeCaseInsensitive(packRoot, relative))
            .FirstOrDefault(path => path is not null && File.Exists(path));
    }

    public static string? FindEssentials(string packRoot) =>
        FileSystemCase.FindDirectory(packRoot, LauncherConstants.GameEssentialsDirectoryName);

    public static string? FindWindowsProxy(string packRoot)
    {
        string? essentials = FindEssentials(packRoot);
        if (essentials is null)
        {
            return null;
        }

        foreach (string root in GetPayloadRoots(essentials))
        {
            string? proxy = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    "winhttp.dll",
                    StringComparison.OrdinalIgnoreCase));
            if (proxy is not null)
            {
                return proxy;
            }
        }

        return null;
    }

    public static string? FindDoorstopDirectory(string packRoot)
    {
        string? essentials = FindEssentials(packRoot);
        if (essentials is null)
        {
            return null;
        }

        foreach (string root in GetPayloadRoots(essentials))
        {
            string? directory = Directory
                .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    "doorstop_libs",
                    StringComparison.OrdinalIgnoreCase));
            if (directory is not null)
            {
                return directory;
            }
        }

        return null;
    }

    public static string? FindUnixDoorstopLibrary(string packRoot)
    {
        string? directory = FindDoorstopDirectory(packRoot);
        if (directory is null)
        {
            return null;
        }

        string[] preferred = OperatingSystem.IsMacOS()
            ? RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? ["libdoorstop_arm64.dylib", "libdoorstop.dylib", "doorstop.dylib"]
                : ["libdoorstop_x64.dylib", "libdoorstop.dylib", "doorstop.dylib"]
            : RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? ["libdoorstop_arm64.so", "libdoorstop.so", "doorstop.so"]
                : ["libdoorstop_x64.so", "libdoorstop.so", "doorstop.so"];

        foreach (string name in preferred)
        {
            string? file = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (file is not null)
            {
                return file;
            }
        }

        return null;
    }

    public static string? FindUnstrippedCorlib(string packRoot)
    {
        string? bepinex = FileSystemCase.FindDirectory(packRoot, "BepInEx")
            ?? FileSystemCase.FindDirectory(packRoot, "bepinex");
        if (bepinex is not null)
        {
            string? insideBepInEx = FileSystemCase.FindDirectory(bepinex, "unstripped_corlib");
            if (insideBepInEx is not null)
            {
                return insideBepInEx;
            }
        }

        return FileSystemCase.FindDirectory(packRoot, "unstripped_corlib");
    }

    public static string? FindExistingUnixBepInExScript(string gameDirectory)
    {
        foreach (string name in new[] { "start_game_bepinex.sh", "run_bepinex.sh" })
        {
            string? script = FileSystemCase.FindFile(gameDirectory, name);
            if (script is not null)
            {
                return script;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetPayloadRoots(string essentials)
    {
        string ridRoot = Path.Combine(essentials, PlatformRid);
        if (Directory.Exists(ridRoot))
        {
            yield return ridRoot;
        }

        yield return essentials;
    }

    private static string? ResolveRelativeCaseInsensitive(string root, string relative)
    {
        string current = Path.GetFullPath(root);
        foreach (string segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
            {
                return null;
            }

            string exact = Path.Combine(current, segment);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            string? matched = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    segment,
                    StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                return null;
            }

            current = matched;
        }

        return current;
    }
}
