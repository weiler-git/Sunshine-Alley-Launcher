using System.Diagnostics;
using SunshineAlley.Core;
using SunshineAlley.Platform.Storage;

namespace SunshineAlley.Platform.Launch;

internal sealed class UnixBepInExLaunchStrategy : IGameLaunchStrategy
{
    private const string WrapperName = "sunshine-alley-launch.sh";
    private readonly ISteamService _steamService;

    public UnixBepInExLaunchStrategy(ISteamService steamService) =>
        _steamService = steamService;

    // Steam must be configured once to invoke the generated wrapper on Unix.
    public bool SupportsAutomaticPersistentInjection => false;

    public async Task<GameLaunchDiagnostics> DiagnoseAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        string? executable = GameLayoutResolver.FindGameExecutable(request.GameDirectory);
        string? preloader = null;
        string? nativeDoorstop = null;
        string? requiredLaunchOption = null;

        if (executable is null)
        {
            problems.Add("The native Valheim executable was not found in the selected game directory.");
        }
        else if (OperatingSystem.IsLinux())
        {
            if (!HasAnyExecuteBit(File.GetUnixFileMode(executable)))
            {
                problems.Add("The native Valheim executable does not have executable permission.");
            }

            DirectoryValidationResult access = await DirectoryWriteAccess.ValidateAsync(
                request.GameDirectory,
                "Valheim game directory",
                cancellationToken);
            if (!access.IsValid)
            {
                problems.Add(access.Error!);
            }
        }

        if (request.ModPackId > 0)
        {
            string packRoot = ModPackService.GetPackRoot(
                request.ModDataDirectory,
                request.ModPackId);
            preloader = GameLayoutResolver.FindPreloader(packRoot);
            nativeDoorstop = GameLayoutResolver.FindUnixDoorstopLibrary(packRoot);
            if (preloader is null)
            {
                problems.Add("The modpack does not contain a BepInEx preloader.");
            }

            if (nativeDoorstop is null)
            {
                problems.Add(
                    $"The modpack does not contain a native Doorstop library for {GameLayoutResolver.PlatformRid}.");
            }

            if (request.UseSteam)
            {
                string script = Path.Combine(request.GameDirectory, WrapperName);
                if (!await _steamService.HasLaunchOptionAsync(WrapperName, cancellationToken))
                {
                    requiredLaunchOption = _steamService.GetRequiredLaunchOption(script);
                    problems.Add(
                        "Steam must be configured once to invoke Sunshine Alley's Unix launch wrapper. "
                        + $"Set Valheim's Steam launch option to: {requiredLaunchOption}");
                }
            }
        }

        return new GameLaunchDiagnostics(
            problems.Count == 0,
            GameLayoutResolver.PlatformRid,
            executable,
            preloader,
            nativeDoorstop,
            requiredLaunchOption,
            problems);
    }

    public async Task<LaunchResult> LaunchAsync(
        GameLaunchRequest request,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        GameLaunchDiagnostics diagnostics = await DiagnoseAsync(request, cancellationToken);
        string? executable = diagnostics.GameExecutable;
        if (executable is null)
        {
            throw new LauncherException(string.Join(Environment.NewLine, diagnostics.Problems));
        }

        if (request.ModPackId > 0
            && (diagnostics.PreloaderPath is null || diagnostics.NativeDoorstopPath is null))
        {
            throw new LauncherException(string.Join(Environment.NewLine, diagnostics.Problems));
        }

        string wrapperPath = Path.Combine(request.GameDirectory, WrapperName);
        string wrapper = await CreateWrapperAsync(request, diagnostics, cancellationToken);
        await WriteWrapperAsync(wrapperPath, wrapper, cancellationToken);

        if (!diagnostics.IsReady)
        {
            // The wrapper is intentionally generated before this error so the launch
            // option shown to the player points at a real, executable file.
            throw new LauncherException(string.Join(Environment.NewLine, diagnostics.Problems));
        }

        await GameProcessWatcher.EnsureNotRunningAsync(executable, cancellationToken);
        IReadOnlyList<string> arguments = GetArguments(request);
        var stopwatch = Stopwatch.StartNew();
        bool launchBegan = false;
        try
        {
            progress?.Report(new LauncherProgress("Starting Valheim…"));
            int processId;
            if (request.UseSteam)
            {
                await _steamService.LaunchAppAsync(arguments, cancellationToken);
                launchBegan = true;
                processId = await GameProcessWatcher.WaitForSteamGameAndExitAsync(
                    executable,
                    cancellationToken);
            }
            else
            {
                await _steamService.EnsureRunningAsync(cancellationToken);
                processId = await StartWrapperAndWaitAsync(
                    wrapperPath,
                    executable,
                    request.GameDirectory,
                    arguments,
                    cancellationToken);
                launchBegan = true;
            }

            stopwatch.Stop();
            return new LaunchResult(processId, request.UseSteam, stopwatch.Elapsed);
        }
        finally
        {
            if (request.ModPackId > 0 && (!request.Persistent || !launchBegan))
            {
                // Leave a pass-through wrapper in place because Steam may still be
                // configured to call it on the player's next vanilla launch.
                await WriteWrapperAsync(
                    wrapperPath,
                    CreatePassThroughWrapper(),
                    CancellationToken.None);
            }
        }
    }

    private static async Task<string> CreateWrapperAsync(
        GameLaunchRequest request,
        GameLaunchDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        if (request.ModPackId > 0)
        {
            string packRoot = ModPackService.GetPackRoot(
                request.ModDataDirectory,
                request.ModPackId);
            return CreateCustomWrapper(
                diagnostics.PreloaderPath!,
                diagnostics.NativeDoorstopPath!,
                GameLayoutResolver.FindUnstrippedCorlib(packRoot));
        }

        if (request.ModPackId == LauncherConstants.PrivateWorldModPackId)
        {
            string? existing = GameLayoutResolver.FindExistingUnixBepInExScript(
                request.GameDirectory);
            if (existing is not null)
            {
                return "#!/bin/sh\nexec " + ShellQuote(existing) + " \"$@\"\n";
            }
        }

        await Task.CompletedTask;
        return CreatePassThroughWrapper();
    }

    private static string CreateCustomWrapper(
        string preloader,
        string nativeDoorstop,
        string? corlib)
    {
        string libraryDirectory = Path.GetDirectoryName(nativeDoorstop)!;
        var lines = new List<string>
        {
            "#!/bin/sh",
            "# Generated by Sunshine Alley Launcher. Configure Steam to call this wrapper before %command%.",
            "export DOORSTOP_ENABLE=TRUE",
            $"export DOORSTOP_INVOKE_DLL_PATH={ShellQuote(Path.GetFullPath(preloader))}"
        };

        if (!string.IsNullOrWhiteSpace(corlib))
        {
            lines.Add($"export DOORSTOP_CORLIB_OVERRIDE_PATH={ShellQuote(Path.GetFullPath(corlib))}");
        }

        if (OperatingSystem.IsMacOS())
        {
            lines.Add(
                $"export DYLD_LIBRARY_PATH={ShellQuote(libraryDirectory)}:\"${{DYLD_LIBRARY_PATH:-}}\"");
            lines.Add(
                $"export DYLD_INSERT_LIBRARIES={ShellQuote(nativeDoorstop)}:\"${{DYLD_INSERT_LIBRARIES:-}}\"");
        }
        else
        {
            lines.Add(
                $"export LD_LIBRARY_PATH={ShellQuote(libraryDirectory)}:\"${{LD_LIBRARY_PATH:-}}\"");
            lines.Add(
                $"export LD_PRELOAD={ShellQuote(nativeDoorstop)}:\"${{LD_PRELOAD:-}}\"");
        }

        lines.Add("exec \"$@\"");
        return string.Join("\n", lines) + "\n";
    }

    private static string CreatePassThroughWrapper() =>
        "#!/bin/sh\n# Sunshine Alley pass-through mode (Doorstop disabled).\nunset DOORSTOP_ENABLE\nunset DOORSTOP_INVOKE_DLL_PATH\nexec \"$@\"\n";

    private static async Task WriteWrapperAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        if (PathSecurity.ContainsReparsePoint(path))
        {
            throw new LauncherException(
                $"Refusing to write the launch wrapper through a symbolic link or junction: '{path}'.");
        }

        string temporary = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellationToken);
            File.SetUnixFileMode(
                temporary,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
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

    private static async Task<int> StartWrapperAndWaitAsync(
        string wrapper,
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = wrapper,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(executable);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new LauncherException("The Valheim launch wrapper could not be started.");
        int id = process.Id;
        await process.WaitForExitAsync(cancellationToken);
        return id;
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static bool HasAnyExecuteBit(UnixFileMode mode) =>
        (mode & (UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute)) != 0;
}
