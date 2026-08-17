namespace SunshineAlley.Core;

public static class SettingsKeys
{
    public const string ModDataDirectory = "InstallDirectory";
    public const string GameDirectory = "GameDirectory";
    public const string UseSteam = "UseSteam";
    public const string Persistent = "Persistent";
    public const string SelectedServer = "SelectedServer";
    public const string FallbackDeviceId = "DeviceIdentity.FallbackGuid";
    public const string WindowsRegistryMigration = "Migration.WindowsRegistryV1";

    public static string OptionalKnown(int modPackId, string name) =>
        $"Optional[{modPackId}][{name}].Known";

    public static string OptionalEnabled(int modPackId, string name) =>
        $"Optional[{modPackId}][{name}].Enabled";

    public static string RsaPublic(string deviceId) => $"RSA[{deviceId}].Public";
    public static string RsaSignature(string deviceId) => $"RSA[{deviceId}].Signature";
}

public sealed class LauncherSettings
{
    private readonly ISettingsStore _store;
    private readonly string _defaultModDataDirectory;

    public LauncherSettings(ISettingsStore store, string defaultModDataDirectory)
    {
        _store = store;
        _defaultModDataDirectory = defaultModDataDirectory;
    }

    public async Task<LauncherPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        string modDataDirectory =
            await _store.GetAsync<string>(SettingsKeys.ModDataDirectory, cancellationToken)
            ?? _defaultModDataDirectory;

        return new LauncherPreferences(
            modDataDirectory,
            await _store.GetAsync<string>(SettingsKeys.GameDirectory, cancellationToken) ?? string.Empty,
            await _store.GetAsync<bool?>(SettingsKeys.UseSteam, cancellationToken) ?? true,
            await _store.GetAsync<bool?>(SettingsKeys.Persistent, cancellationToken) ?? false,
            await _store.GetAsync<string>(SettingsKeys.SelectedServer, cancellationToken) ?? string.Empty);
    }

    public async Task SaveAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        await _store.SetAsync(SettingsKeys.ModDataDirectory, preferences.ModDataDirectory, cancellationToken);
        await _store.SetAsync(SettingsKeys.GameDirectory, preferences.GameDirectory, cancellationToken);
        await _store.SetAsync(SettingsKeys.UseSteam, preferences.UseSteam, cancellationToken);
        await _store.SetAsync(SettingsKeys.Persistent, preferences.Persistent, cancellationToken);
        await _store.SetAsync(SettingsKeys.SelectedServer, preferences.SelectedServer, cancellationToken);
    }
}
