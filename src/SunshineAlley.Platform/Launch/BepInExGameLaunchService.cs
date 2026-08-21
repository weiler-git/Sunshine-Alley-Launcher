using SunshineAlley.Core;

namespace SunshineAlley.Platform.Launch;

public sealed class BepInExGameLaunchService : IGameLaunchService
{
    private readonly IGameLaunchStrategy _strategy;

    public BepInExGameLaunchService(ISteamService steamService)
    {
        _strategy = OperatingSystem.IsWindows()
            ? new WindowsBepInExLaunchStrategy(steamService)
            : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? new UnixBepInExLaunchStrategy(steamService)
                : throw new PlatformNotSupportedException(
                    "Valheim launching is supported on Windows, Linux, and macOS.");
    }

    public bool SupportsAutomaticPersistentInjection =>
        _strategy.SupportsAutomaticPersistentInjection;

    public Task<GameLaunchDiagnostics> DiagnoseAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken = default) =>
        _strategy.DiagnoseAsync(request, cancellationToken);

    public Task<LaunchResult> LaunchAsync(
        GameLaunchRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _strategy.LaunchAsync(request, progress, cancellationToken);
}
