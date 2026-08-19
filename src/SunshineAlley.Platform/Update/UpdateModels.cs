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
