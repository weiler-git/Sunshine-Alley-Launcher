using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SunshineAlley.Core;

public static class LauncherConstants
{
    public const int SteamAppId = 892970;
    public const int PrivateWorldModPackId = -1;
    public const int VanillaModPackId = -99;
    public const string ModPacksDirectoryName = "ModPacks";
    public const string GameEssentialsDirectoryName = "GameEssentials";
    public static readonly Uri ServiceBaseUri = new("https://sunshinealley.games/");
}

public sealed record DeviceIdentity(string Value, DeviceIdentitySource Source, bool IsFallback);

public enum DeviceIdentitySource
{
    WindowsMachineGuid,
    LinuxMachineId,
    MacPlatformUuid,
    PersistedFallback
}

public sealed record SteamInstallation(
    string RootDirectory,
    string? ExecutablePath,
    string? GameDirectory,
    int? BuildId);

public sealed record GameServer(
    string WorldName,
    int ModPackId,
    int SteamBuildId,
    DateTime ReleaseWorld,
    DateTime LastOnline,
    bool IsLocalOption = false);

public sealed class ModPackState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OptionalModState> _optionalMods =
        new(StringComparer.OrdinalIgnoreCase);

    public ModPackState(int id) => Id = id;

    public int Id { get; }
    public bool IsVerified { get; private set; }

    public IReadOnlyList<OptionalModState> OptionalMods
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<OptionalModState>(
                    _optionalMods.Values.OrderBy(item => item.Name).ToList());
            }
        }
    }

    public void SetVerified(bool value)
    {
        lock (_gate)
        {
            IsVerified = value;
        }
    }

    public void ReplaceOptionalMods(IEnumerable<OptionalModState> items)
    {
        lock (_gate)
        {
            _optionalMods.Clear();
            foreach (OptionalModState item in items)
            {
                _optionalMods[item.Name] = item;
            }
        }
    }

    public bool SetOptionalEnabled(string name, bool enabled)
    {
        lock (_gate)
        {
            if (!_optionalMods.TryGetValue(name, out OptionalModState? existing))
            {
                return false;
            }

            _optionalMods[name] = existing with { Enabled = enabled };
            IsVerified = false;
            return true;
        }
    }
}

public sealed record OptionalModState(string Name, bool Enabled);

public sealed record LauncherPreferences(
    string ModDataDirectory,
    string GameDirectory,
    bool UseSteam,
    bool Persistent,
    string SelectedServer);

public sealed record LauncherProgress(string Message, long Completed = 0, long Total = 0)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}

public sealed record GameLaunchRequest(
    string GameDirectory,
    string ModDataDirectory,
    int ModPackId,
    bool UseSteam,
    bool Persistent,
    IReadOnlyList<string>? AdditionalArguments = null);

public sealed record GameLaunchDiagnostics(
    bool IsReady,
    string RuntimeIdentifier,
    string? GameExecutable,
    string? PreloaderPath,
    string? NativeDoorstopPath,
    string? RequiredSteamLaunchOption,
    IReadOnlyList<string> Problems);

public sealed record LaunchResult(int? ProcessId, bool StartedThroughSteam, TimeSpan Runtime);

public sealed class ServerListRequest
{
    [JsonPropertyName("SessionGuid")]
    public required string SessionGuid { get; init; }

    [JsonPropertyName("PublicKey")]
    public string? PublicKey { get; init; }
}

public sealed class ServerListResponse
{
    [JsonPropertyName("AuthLevel")]
    public int AuthLevel { get; init; }

    [JsonPropertyName("IsAdmin")]
    public bool IsAdmin { get; init; }

    [JsonPropertyName("servers")]
    public List<ServerDto> Servers { get; init; } = [];
}

public sealed class ServerDto
{
    [JsonPropertyName("WorldName")]
    public string WorldName { get; init; } = string.Empty;

    [JsonPropertyName("ModPack")]
    public int ModPack { get; init; }

    [JsonPropertyName("SteamBuildID")]
    public int SteamBuildId { get; init; }

    [JsonPropertyName("AllowNewCharacters")]
    public bool AllowNewCharacters { get; init; }

    [JsonPropertyName("ReleaseWorld")]
    public DateTime ReleaseWorld { get; init; }

    [JsonPropertyName("LastOnline")]
    public DateTime LastOnline { get; init; }
}

public sealed class VerifyModPackRequest
{
    [JsonPropertyName("IsAdmin")]
    public bool IsAdmin { get; init; }

    [JsonPropertyName("ModPack")]
    public int ModPack { get; init; }

    [JsonPropertyName("RuntimeIdentifier")]
    public required string RuntimeIdentifier { get; init; }

    [JsonPropertyName("Files")]
    public List<FileHashDto> Files { get; init; } = [];
}

public sealed class FileHashDto
{
    [JsonPropertyName("FilePath")]
    public required string FilePath { get; init; }

    [JsonPropertyName("Hash")]
    public required string Hash { get; init; }

    // Keep the legacy misspelling because it is part of the existing API contract.
    [JsonPropertyName("Extenstion")]
    public required string Extenstion { get; init; }
}

public sealed class FileValidationResponse
{
    [JsonPropertyName("modPack")]
    public int ModPack { get; init; }

    [JsonPropertyName("Illegal")]
    public List<IllegalFileDto> Illegal { get; init; } = [];

    [JsonPropertyName("Missing")]
    public List<ServerFileDto> Missing { get; init; } = [];

    [JsonPropertyName("Optional")]
    public List<ServerFileDto> Optional { get; init; } = [];
}

public sealed class IllegalFileDto
{
    [JsonPropertyName("File")]
    public string File { get; init; } = string.Empty;
}

public sealed class ServerFileDto
{
    [JsonPropertyName("DownloadUrl")]
    public string DownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("ModName")]
    public string ModName { get; init; } = string.Empty;

    [JsonPropertyName("OK")]
    public bool Ok { get; init; }
}

public class LauncherException : Exception
{
    public LauncherException(string message) : base(message)
    {
    }

    public LauncherException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class SecretStoreUnavailableException : LauncherException
{
    public SecretStoreUnavailableException(string message) : base(message)
    {
    }
}
