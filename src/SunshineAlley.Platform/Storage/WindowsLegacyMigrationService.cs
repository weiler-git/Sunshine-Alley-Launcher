using Microsoft.Win32;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Storage;

public sealed class WindowsLegacyMigrationService : ILegacyMigrationService
{
    private const string RegistryPath = @"SOFTWARE\Sunshine Alley";
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;

    public WindowsLegacyMigrationService(
        ISettingsStore settingsStore,
        ISecretStore secretStore)
    {
        _settingsStore = settingsStore;
        _secretStore = secretStore;
    }

    public async Task MigrateAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()
            || await _settingsStore.GetAsync<bool?>(
                SettingsKeys.WindowsRegistryMigration,
                cancellationToken) == true)
        {
            return;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        if (key is null)
        {
            await MarkCompleteAsync(cancellationToken);
            return;
        }

        string privateKeyName = $"RSA[{deviceId}].Private";
        string publicKeyName = SettingsKeys.RsaPublic(deviceId);
        string signatureName = SettingsKeys.RsaSignature(deviceId);
        bool layoutMigrated = await _settingsStore.GetAsync<bool?>(
            SettingsKeys.WindowsLayoutMigration,
            cancellationToken) == true;
        string? configuredDataDirectory = await _settingsStore.GetAsync<string>(
            SettingsKeys.ModDataDirectory,
            cancellationToken);
        string? configuredGameDirectory = await _settingsStore.GetAsync<string>(
            SettingsKeys.GameDirectory,
            cancellationToken);

        if (key.GetValue(privateKeyName) is string privateXml
            && !string.IsNullOrWhiteSpace(privateXml))
        {
            if (!await _secretStore.IsAvailableAsync(cancellationToken))
            {
                throw new SecretStoreUnavailableException(
                    "Windows Credential Manager is unavailable, so the legacy RSA private key was not migrated.");
            }

            string secretName = RsaIdentityService.GetSecretName(deviceId);
            if (await _secretStore.GetAsync(secretName, cancellationToken) is null)
            {
                await _secretStore.SetAsync(
                    secretName,
                    RsaXmlKey.ConvertPrivateXmlToPkcs8(privateXml),
                    cancellationToken);
            }

            // The OS credential now owns the private key. Keep non-secret legacy
            // settings for transition/rollback, but do not leave private material
            // in the registry.
            key.DeleteValue(privateKeyName, false);
        }

        foreach (string valueName in key.GetValueNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    valueName,
                    SettingsKeys.ModDataDirectory,
                    StringComparison.OrdinalIgnoreCase)
                && (layoutMigrated || !string.IsNullOrWhiteSpace(configuredDataDirectory)))
            {
                // V2 separates application and data directories. Do not let the
                // legacy combined InstallDirectory overwrite the migrated path.
                continue;
            }

            if (string.Equals(
                    valueName,
                    SettingsKeys.GameDirectory,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(configuredGameDirectory))
            {
                // Guided setup has already validated this choice; do not replace
                // it with an older registry path on first installed startup.
                continue;
            }

            if (valueName.StartsWith("RSA[", StringComparison.OrdinalIgnoreCase)
                && valueName.EndsWith("].Private", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            object? raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null)
            {
                continue;
            }

            string text = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty;
            if (bool.TryParse(text, out bool boolean))
            {
                await _settingsStore.SetAsync(valueName, boolean, cancellationToken);
            }
            else
            {
                await _settingsStore.SetAsync(valueName, text, cancellationToken);
            }
        }

        if (key.GetValue(publicKeyName) is string publicKey)
        {
            await _settingsStore.SetAsync(publicKeyName, publicKey, cancellationToken);
        }

        if (key.GetValue(signatureName) is string signature)
        {
            await _settingsStore.SetAsync(signatureName, signature, cancellationToken);
        }

        // Non-secret registry settings are retained for transition/rollback.
        await MarkCompleteAsync(cancellationToken);
    }

    private Task MarkCompleteAsync(CancellationToken cancellationToken) =>
        _settingsStore.SetAsync(
            SettingsKeys.WindowsRegistryMigration,
            true,
            cancellationToken);
}
