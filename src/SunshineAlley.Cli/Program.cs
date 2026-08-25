using System.Runtime.InteropServices;
using SunshineAlley.Core;
using SunshineAlley.Platform;

namespace SunshineAlley.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        if (args[0].ToLowerInvariant() is "launcher-version" or "lversion")
        {
            return PrintLauncherVersion();
        }

        try
        {
            await using LauncherRuntime runtime = await LauncherRuntime.CreateAsync();
            LauncherPreferences preferences = await runtime.LoadPreferencesWithDiscoveryAsync();
            return args[0].ToLowerInvariant() switch
            {
                "doctor" => await DoctorAsync(runtime, preferences, args[1..]),
                "servers" => await ServersAsync(runtime),
                "verify" => await VerifyAsync(runtime, preferences, args[1..]),
                "launch" => await LaunchAsync(runtime, preferences, args[1..]),
                "device-id" => PrintDeviceId(runtime),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("ERROR: " + exception.Message);
            return 1;
        }
    }

    private static async Task<int> DoctorAsync(
        LauncherRuntime runtime,
        LauncherPreferences preferences,
        string[] args)
    {
        int modPackId = ReadModPackId(args, LauncherConstants.VanillaModPackId);
        SteamInstallation? steam = await runtime.Steam.DiscoverAsync();
        var request = new GameLaunchRequest(
            ReadOption(args, "--game") ?? preferences.GameDirectory,
            ReadOption(args, "--data") ?? preferences.ModDataDirectory,
            modPackId,
            !args.Contains("--no-steam", StringComparer.OrdinalIgnoreCase),
            args.Contains("--persistent", StringComparer.OrdinalIgnoreCase));
        GameLaunchDiagnostics diagnostics = await runtime.GameLauncher.DiagnoseAsync(request);

        Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"RID: {diagnostics.RuntimeIdentifier}");
        Console.WriteLine($"Device identity: {runtime.DeviceIdentity.Source} (fallback: {runtime.DeviceIdentity.IsFallback})");
        Console.WriteLine($"Steam root: {steam?.RootDirectory ?? "not found"}");
        Console.WriteLine($"Steam build: {steam?.BuildId?.ToString() ?? "unknown"}");
        Console.WriteLine($"Game executable: {diagnostics.GameExecutable ?? "not found"}");
        Console.WriteLine($"BepInEx preloader: {diagnostics.PreloaderPath ?? "not required/not found"}");
        Console.WriteLine($"Doorstop native payload: {diagnostics.NativeDoorstopPath ?? "not required/not found"}");
        if (diagnostics.RequiredSteamLaunchOption is not null)
        {
            Console.WriteLine($"Required Steam launch option: {diagnostics.RequiredSteamLaunchOption}");
        }

        foreach (string problem in diagnostics.Problems)
        {
            Console.WriteLine("PROBLEM: " + problem);
        }

        Console.WriteLine(diagnostics.IsReady ? "READY" : "NOT READY");
        return diagnostics.IsReady ? 0 : 2;
    }

    private static async Task<int> ServersAsync(LauncherRuntime runtime)
    {
        IReadOnlyList<GameServer> servers = await runtime.Servers.RefreshAsync();
        foreach (GameServer server in servers)
        {
            Console.WriteLine(
                $"{server.WorldName} | modpack={server.ModPackId} | steam-build={server.SteamBuildId}");
        }

        return 0;
    }

    private static async Task<int> VerifyAsync(
        LauncherRuntime runtime,
        LauncherPreferences preferences,
        string[] args)
    {
        int modPackId = ReadModPackId(args, 0);
        if (modPackId <= 0)
        {
            Console.Error.WriteLine("verify requires a positive modpack ID.");
            return 2;
        }

        string data = ReadOption(args, "--data") ?? preferences.ModDataDirectory;
        data = await EnsureModDataDirectoryAsync(
            runtime,
            data,
            preferences.GameDirectory);
        var progress = new Progress<LauncherProgress>(PrintProgress);
        await runtime.Servers.RefreshAsync();
        await runtime.ModPacks.EnsureVerifiedAsync(
            modPackId,
            data,
            runtime.Servers.IsAdmin,
            progress);
        Console.WriteLine($"Modpack {modPackId} is verified.");
        return 0;
    }

    private static async Task<int> LaunchAsync(
        LauncherRuntime runtime,
        LauncherPreferences preferences,
        string[] args)
    {
        int modPackId = ReadModPackId(args, LauncherConstants.VanillaModPackId);
        string game = ReadOption(args, "--game") ?? preferences.GameDirectory;
        string data = ReadOption(args, "--data") ?? preferences.ModDataDirectory;
        bool useSteam = !args.Contains("--no-steam", StringComparer.OrdinalIgnoreCase);
        bool persistent = args.Contains("--persistent", StringComparer.OrdinalIgnoreCase);
        var progress = new Progress<LauncherProgress>(PrintProgress);

        if (modPackId > 0)
        {
            data = await EnsureModDataDirectoryAsync(runtime, data, game);
            await runtime.Servers.RefreshAsync();
            await runtime.ModPacks.EnsureVerifiedAsync(
                modPackId,
                data,
                runtime.Servers.IsAdmin,
                progress);
        }

        var request = new GameLaunchRequest(game, data, modPackId, useSteam, persistent);
        GameLaunchDiagnostics diagnostics = await runtime.GameLauncher.DiagnoseAsync(request);
        if (args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(diagnostics.IsReady ? "READY" : "NOT READY");
            foreach (string problem in diagnostics.Problems)
            {
                Console.WriteLine("PROBLEM: " + problem);
            }

            return diagnostics.IsReady ? 0 : 2;
        }

        LaunchResult result = await runtime.GameLauncher.LaunchAsync(request, progress);
        Console.WriteLine($"Valheim process {result.ProcessId} exited after {result.Runtime:g}.");
        return 0;
    }

    private static async Task<string> EnsureModDataDirectoryAsync(
        LauncherRuntime runtime,
        string modDataDirectory,
        string gameDirectory)
    {
        DirectoryValidationResult result = await runtime.ModDataDirectories.ValidateAsync(
            modDataDirectory,
            gameDirectory,
            true);
        if (!result.IsValid)
        {
            throw new LauncherException(result.Error!);
        }

        return result.NormalizedPath;
    }

    private static int PrintLauncherVersion()
    {
        string version = typeof(Program).Assembly
            .GetName()
            .Version?
            .ToString(3)
            ?? "unknown (3.0.0 or later)";

        Console.WriteLine(version);
        return 0;
    }

    private static int PrintDeviceId(LauncherRuntime runtime)
    {
        Console.WriteLine(runtime.DeviceIdentity.Value);
        return 0;
    }

    private static void PrintProgress(LauncherProgress progress)
    {
        string suffix = progress.Total > 0
            ? $" ({progress.Completed}/{progress.Total})"
            : string.Empty;
        Console.WriteLine(progress.Message + suffix);
    }

    private static int ReadModPackId(string[] args, int fallback)
    {
        string? raw = ReadOption(args, "--modpack")
            ?? args.FirstOrDefault(value => int.TryParse(value, out _));
        return int.TryParse(raw, out int value) ? value : fallback;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Sunshine Alley cross-platform launch harness");
        Console.WriteLine();
        Console.WriteLine("  launcher-version/lversion");
        Console.WriteLine("  doctor [--modpack ID] [--game PATH] [--data PATH] [--no-steam] [--persistent]");
        Console.WriteLine("  servers");
        Console.WriteLine("  verify --modpack ID [--data PATH]");
        Console.WriteLine("  launch --modpack ID [--game PATH] [--data PATH] [--no-steam] [--persistent] [--dry-run]");
        Console.WriteLine("  device-id");
    }
}
