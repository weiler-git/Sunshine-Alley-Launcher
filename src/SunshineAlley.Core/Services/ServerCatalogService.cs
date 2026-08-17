namespace SunshineAlley.Core;

public sealed class ServerCatalogService
{
    private readonly LauncherApiClient _apiClient;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly string _sessionGuid = Guid.NewGuid().ToString();

    public ServerCatalogService(LauncherApiClient apiClient) => _apiClient = apiClient;

    public int AuthLevel { get; private set; }
    public bool IsAdmin { get; private set; }

    public async Task<IReadOnlyList<GameServer>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            ServerListResponse response = await _apiClient.GetServerListAsync(
                _sessionGuid,
                AuthLevel,
                cancellationToken);

            AuthLevel = response.AuthLevel;
            IsAdmin = response.IsAdmin;

            var servers = response.Servers
                .Where(item => AuthLevel >= 2 || item.AllowNewCharacters)
                .Select(item => new GameServer(
                    item.WorldName,
                    item.ModPack,
                    item.SteamBuildId,
                    item.ReleaseWorld,
                    item.LastOnline))
                .ToList();

            servers.Add(new GameServer(
                "Private World",
                LauncherConstants.PrivateWorldModPackId,
                0,
                DateTime.MinValue,
                DateTime.UtcNow,
                true));
            servers.Add(new GameServer(
                "Vanilla / No mods",
                LauncherConstants.VanillaModPackId,
                0,
                DateTime.MinValue,
                DateTime.UtcNow,
                true));

            return servers;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}

public static class SteamBuildCompatibility
{
    public static bool IsCompatible(int? localBuildId, int serverBuildId) =>
        serverBuildId == 0 || localBuildId is null or < 0 || localBuildId == serverBuildId;
}
