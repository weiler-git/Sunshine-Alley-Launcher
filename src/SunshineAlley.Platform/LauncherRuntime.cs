using System.Net;
using SunshineAlley.Core;
using SunshineAlley.Platform.Identity;
using SunshineAlley.Platform.Launch;
using SunshineAlley.Platform.Secrets;
using SunshineAlley.Platform.Shell;
using SunshineAlley.Platform.Steam;
using SunshineAlley.Platform.Storage;

namespace SunshineAlley.Platform;

public sealed class LauncherRuntime : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LauncherIdentity _identity;

    private LauncherRuntime(
        PlatformPaths paths,
        JsonSettingsStore settingsStore,
        LauncherSettings settings,
        ModDataDirectoryPolicy modDataDirectories,
        DeviceIdentity deviceIdentity,
        LauncherIdentity identity,
        HttpClient httpClient,
        SteamService steam,
        ShellService shell,
        ServerCatalogService servers,
        ModPackService modPacks,
        BepInExGameLaunchService gameLauncher)
    {
        Paths = paths;
        SettingsStore = settingsStore;
        Settings = settings;
        ModDataDirectories = modDataDirectories;
        DeviceIdentity = deviceIdentity;
        _identity = identity;
        _httpClient = httpClient;
        Steam = steam;
        Shell = shell;
        Servers = servers;
        ModPacks = modPacks;
        GameLauncher = gameLauncher;
    }

    public PlatformPaths Paths { get; }
    public ISettingsStore SettingsStore { get; }
    public LauncherSettings Settings { get; }
    public IModDataDirectoryPolicy ModDataDirectories { get; }
    public DeviceIdentity DeviceIdentity { get; }
    public ISteamService Steam { get; }
    public IShellService Shell { get; }
    public ServerCatalogService Servers { get; }
    public ModPackService ModPacks { get; }
    public IGameLaunchService GameLauncher { get; }

    public static async Task<LauncherRuntime> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        PlatformPaths paths = PlatformPaths.CreateDefault();
        paths.EnsureDirectories();
        var settingsStore = new JsonSettingsStore(paths.SettingsFile);
        ISecretStore secretStore = SecretStoreFactory.Create();
        var deviceService = new DeviceIdentityService(settingsStore);
        DeviceIdentity deviceIdentity = await deviceService.GetAsync(cancellationToken);

        var migration = new WindowsLegacyMigrationService(settingsStore, secretStore);
        await migration.MigrateAsync(deviceIdentity.Value, cancellationToken);

        var identityService = new RsaIdentityService(secretStore, settingsStore);
        LauncherIdentity identity = await identityService.LoadOrCreateAsync(
            deviceIdentity.Value,
            cancellationToken);

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SunshineAlleyLauncher/3.0");

        var api = new LauncherApiClient(httpClient, identity);
        var steam = new SteamService();
        var shell = new ShellService();
        var settings = new LauncherSettings(settingsStore, paths.DataDirectory);
        var modDataDirectories = new ModDataDirectoryPolicy();
        var servers = new ServerCatalogService(api);
        var modPacks = new ModPackService(api, httpClient, settingsStore);
        var gameLauncher = new BepInExGameLaunchService(steam);

        return new LauncherRuntime(
            paths,
            settingsStore,
            settings,
            modDataDirectories,
            deviceIdentity,
            identity,
            httpClient,
            steam,
            shell,
            servers,
            modPacks,
            gameLauncher);
    }

    public async Task<LauncherPreferences> LoadPreferencesWithDiscoveryAsync(
        CancellationToken cancellationToken = default)
    {
        LauncherPreferences preferences = await Settings.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(preferences.GameDirectory)
            && Directory.Exists(preferences.GameDirectory))
        {
            return preferences;
        }

        SteamInstallation? installation = await Steam.DiscoverAsync(cancellationToken);
        if (installation?.GameDirectory is null || !Directory.Exists(installation.GameDirectory))
        {
            return preferences;
        }

        LauncherPreferences discovered = preferences with
        {
            GameDirectory = installation.GameDirectory
        };
        await Settings.SaveAsync(discovered, cancellationToken);
        return discovered;
    }

    public ValueTask DisposeAsync()
    {
        _identity.Dispose();
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
