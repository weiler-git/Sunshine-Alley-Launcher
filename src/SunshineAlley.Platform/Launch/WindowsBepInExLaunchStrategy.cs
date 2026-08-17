using System.Diagnostics;
using System.Text;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Launch;

internal sealed class WindowsBepInExLaunchStrategy : IGameLaunchStrategy
{
    private readonly ISteamService _steamService;

    public WindowsBepInExLaunchStrategy(ISteamService steamService) =>
        _steamService = steamService;

    public bool SupportsAutomaticPersistentInjection => true;

    public Task<GameLaunchDiagnostics> DiagnoseAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problems = new List<string>();
        string? executable = GameLayoutResolver.FindGameExecutable(request.GameDirectory);
        string? preloader = null;
        string? proxy = null;

        if (executable is null)
        {
            problems.Add("valheim.exe was not found in the selected game directory.");
        }

        if (request.ModPackId > 0)
        {
            string packRoot = ModPackService.GetPackRoot(
                request.ModDataDirectory,
                request.ModPackId);
            preloader = GameLayoutResolver.FindPreloader(packRoot);
            proxy = GameLayoutResolver.FindWindowsProxy(packRoot);
            if (preloader is null)
            {
                problems.Add("The modpack does not contain a BepInEx preloader.");
            }

            if (proxy is null)
            {
                problems.Add(
                    $"The modpack does not contain the {GameLayoutResolver.PlatformRid} winhttp.dll Doorstop proxy.");
            }

            if (GameLayoutResolver.FindDoorstopDirectory(packRoot) is null)
            {
                problems.Add("The modpack does not contain a doorstop_libs directory.");
            }
        }

        return Task.FromResult(new GameLaunchDiagnostics(
            problems.Count == 0,
            GameLayoutResolver.PlatformRid,
            executable,
            preloader,
            proxy,
            null,
            problems));
    }

    public async Task<LaunchResult> LaunchAsync(
        GameLaunchRequest request,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        GameLaunchDiagnostics diagnostics = await DiagnoseAsync(request, cancellationToken);
        if (!diagnostics.IsReady || diagnostics.GameExecutable is null)
        {
            throw new LauncherException(string.Join(Environment.NewLine, diagnostics.Problems));
        }

        await GameProcessWatcher.EnsureNotRunningAsync(
            diagnostics.GameExecutable,
            cancellationToken);
        var deployment = new FileDeploymentManager(request.GameDirectory);
        bool customPack = request.ModPackId > 0;
        bool managedProfile = customPack
            || request.ModPackId == LauncherConstants.VanillaModPackId;
        bool launchBegan = false;

        try
        {
            if (customPack)
            {
                progress?.Report(new LauncherProgress("Preparing Windows Doorstop injection…"));
                await deployment.BeginAsync(request.Persistent, cancellationToken);
                string packRoot = ModPackService.GetPackRoot(
                    request.ModDataDirectory,
                    request.ModPackId);
                string proxy = diagnostics.NativeDoorstopPath!;
                string doorstopDirectory = GameLayoutResolver.FindDoorstopDirectory(packRoot)!;

                await deployment.DeployFileAsync(proxy, "winhttp.dll", cancellationToken);
                foreach (string source in Directory.EnumerateFiles(
                    doorstopDirectory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(doorstopDirectory, source);
                    await deployment.DeployFileAsync(
                        source,
                        Path.Combine("doorstop_libs", relative),
                        cancellationToken);
                }

                string configuration = CreateDoorstopConfiguration(
                    diagnostics.PreloaderPath!,
                    GameLayoutResolver.FindUnstrippedCorlib(packRoot));
                await deployment.DeployTextAsync(
                    configuration,
                    "doorstop_config.ini",
                    cancellationToken);
            }
            else if (request.ModPackId == LauncherConstants.VanillaModPackId)
            {
                progress?.Report(new LauncherProgress("Preparing a vanilla launch…"));
                await deployment.BeginAsync(request.Persistent, cancellationToken);
                await deployment.DeployTextAsync(
                    CreateDisabledDoorstopConfiguration(),
                    "doorstop_config.ini",
                    cancellationToken);
            }
            else
            {
                // This removes a persistent custom deployment and restores any installation
                // that was present before Sunshine Alley touched the game directory.
                await deployment.RestoreAsync(cancellationToken);
            }
        }
        catch
        {
            if (managedProfile)
            {
                try
                {
                    await deployment.RestoreAsync(CancellationToken.None);
                }
                catch
                {
                    // The recovery manifest remains for the next launcher start.
                }
            }

            throw;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            progress?.Report(new LauncherProgress("Starting Valheim…"));
            int processId;
            if (request.UseSteam)
            {
                await _steamService.LaunchAppAsync(GetArguments(request), cancellationToken);
                launchBegan = true;
                processId = await GameProcessWatcher.WaitForSteamGameAndExitAsync(
                    diagnostics.GameExecutable,
                    cancellationToken);
            }
            else
            {
                await _steamService.EnsureRunningAsync(cancellationToken);
                launchBegan = true;
                processId = await StartDirectAndWaitAsync(
                    diagnostics.GameExecutable,
                    request.GameDirectory,
                    GetArguments(request),
                    cancellationToken);
            }

            stopwatch.Stop();
            return new LaunchResult(processId, request.UseSteam, stopwatch.Elapsed);
        }
        finally
        {
            if (managedProfile && (!request.Persistent || !launchBegan))
            {
                progress?.Report(new LauncherProgress("Restoring the original game files…"));
                await deployment.RestoreAsync(CancellationToken.None);
            }
        }
    }

    private static IReadOnlyList<string> GetArguments(GameLaunchRequest request)
    {
        var arguments = new List<string> { "-console" };
        if (request.AdditionalArguments is not null)
        {
            arguments.AddRange(request.AdditionalArguments);
        }

        return arguments;
    }

    private static async Task<int> StartDirectAndWaitAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new LauncherException("The Valheim process could not be started.");
        int id = process.Id;
        await process.WaitForExitAsync(cancellationToken);
        return id;
    }

    private static string CreateDoorstopConfiguration(string preloader, string? corlib)
    {
        var lines = new List<string>
        {
            "# Managed by Sunshine Alley Launcher. Original files are stored in .sunshine-alley-backup.",
            "[General]",
            "enabled = true",
            $"target_assembly={Path.GetFullPath(preloader)}",
            "redirect_output_log = false",
            "boot_config_override =",
            "ignore_disable_switch = false",
            string.Empty,
            "[UnityMono]",
            $"dll_search_path_override = {corlib ?? string.Empty}",
            "debug_enabled = false",
            "debug_address = 127.0.0.1:10000",
            "debug_suspend = false"
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string CreateDisabledDoorstopConfiguration()
    {
        string target = Path.Combine("BepInEx", "core", "BepInEx.Preloader.dll");
        return "# Managed by Sunshine Alley Launcher: vanilla profile.\r\n"
            + "[General]\r\n"
            + "enabled = false\r\n"
            + $"target_assembly={target}\r\n"
            + "redirect_output_log = false\r\n"
            + "ignore_disable_switch = false\r\n";
    }
}
