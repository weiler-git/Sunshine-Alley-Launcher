using System.Text.Json;

namespace SunshineAlley.Core;

public interface ISettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, JsonElement>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);
    Task SetAsync(string name, string value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}

public interface IDeviceIdentityService
{
    Task<DeviceIdentity> GetAsync(CancellationToken cancellationToken = default);
}

public interface IModDataDirectoryPolicy
{
    Task<DirectoryValidationResult> ValidateAsync(
        string candidate,
        string gameDirectory,
        bool initialize,
        CancellationToken cancellationToken = default);
}

public interface ILegacyMigrationService
{
    Task MigrateAsync(string deviceId, CancellationToken cancellationToken = default);
}

public interface ISteamService
{
    Task<SteamInstallation?> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);
    Task EnsureRunningAsync(CancellationToken cancellationToken = default);
    Task LaunchAppAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
    Task<bool> HasLaunchOptionAsync(string marker, CancellationToken cancellationToken = default);
    string GetRequiredLaunchOption(string scriptPath);
}

public interface IShellService
{
    Task OpenFolderAsync(string path, CancellationToken cancellationToken = default);
    Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default);
}

public interface IGameLaunchService
{
    bool SupportsAutomaticPersistentInjection { get; }

    Task<GameLaunchDiagnostics> DiagnoseAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken = default);

    Task<LaunchResult> LaunchAsync(
        GameLaunchRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
