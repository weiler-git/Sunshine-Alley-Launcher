namespace SunshineAlley.Platform.Update;

public sealed class LauncherUpdatePlan
{
    public required string ProductId { get; init; }
    public required string TransactionId { get; init; }
    public required int WaitForProcessId { get; init; }
    public required string InstalledExecutable { get; init; }
    public required string StagedExecutable { get; init; }
    public required string BackupExecutable { get; init; }
    public required string ConfirmationFile { get; init; }
    public required string ExpectedSha256 { get; init; }
    public required string ExpectedPreviousSha256 { get; init; }
    public required string Version { get; init; }
    public required long ReleaseId { get; init; }
    public required string Channel { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public List<string> RestartArguments { get; init; } = [];
}

public sealed record VerifiedUpdateEnvelope(
    SunshineAlley.Core.LauncherUpdateManifest Manifest,
    byte[] PayloadBytes);

internal static class LauncherUpdatePolicy
{
    public static bool IsCurrentVersionUnsupported(
        SunshineAlley.Core.LauncherUpdateManifest manifest,
        string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(manifest.MinimumSupportedVersion))
        {
            return false;
        }

        return IsCurrentVersionUnsupported(
            manifest.MinimumSupportedVersion,
            currentVersion);
    }

    public static bool IsCurrentVersionUnsupported(
        string? minimumSupportedVersion,
        string currentVersion) =>
        !string.IsNullOrWhiteSpace(minimumSupportedVersion)
        && ParseVersion(currentVersion, "current launcher")
            < ParseVersion(
                minimumSupportedVersion,
                "minimum supported launcher");

    public static string? SelectHigherMinimumSupportedVersion(
        string? rememberedVersion,
        string? signedVersion)
    {
        if (string.IsNullOrWhiteSpace(rememberedVersion))
        {
            return string.IsNullOrWhiteSpace(signedVersion) ? null : signedVersion;
        }

        if (string.IsNullOrWhiteSpace(signedVersion))
        {
            return rememberedVersion;
        }

        return ParseVersion(signedVersion, "signed minimum supported launcher")
            > ParseVersion(rememberedVersion, "remembered minimum supported launcher")
                ? signedVersion
                : rememberedVersion;
    }

    public static Version ParseVersion(string value, string description)
    {
        string numeric = value.Split('-', 2)[0].Split('+', 2)[0];
        return Version.TryParse(numeric, out Version? version)
            ? version
            : throw new SunshineAlley.Core.LauncherException(
                $"The {description} version '{value}' is invalid.");
    }
}
