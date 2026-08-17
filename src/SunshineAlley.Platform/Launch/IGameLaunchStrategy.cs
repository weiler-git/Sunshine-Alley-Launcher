using SunshineAlley.Core;

namespace SunshineAlley.Platform.Launch;

internal interface IGameLaunchStrategy
{
    bool SupportsAutomaticPersistentInjection { get; }

    Task<GameLaunchDiagnostics> DiagnoseAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken);

    Task<LaunchResult> LaunchAsync(
        GameLaunchRequest request,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);
}
