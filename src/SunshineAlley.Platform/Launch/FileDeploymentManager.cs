using System.Text.Json;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Launch;

internal sealed class FileDeploymentManager
{
    private const string BackupDirectoryName = ".sunshine-alley-backup";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _gameRoot;
    private readonly string _backupRoot;
    private readonly string _manifestPath;
    private DeploymentManifest? _manifest;

    public FileDeploymentManager(string gameRoot)
    {
        _gameRoot = Path.GetFullPath(gameRoot);
        _backupRoot = Path.Combine(_gameRoot, BackupDirectoryName);
        _manifestPath = Path.Combine(_backupRoot, "deployment.json");
    }

    public async Task BeginAsync(bool persistent, CancellationToken cancellationToken)
    {
        await RestoreAsync(cancellationToken);
        Directory.CreateDirectory(_backupRoot);
        _manifest = new DeploymentManifest { Persistent = persistent };
        await SaveManifestAsync(cancellationToken);
    }

    public async Task DeployFileAsync(
        string source,
        string relativeTarget,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Required injection payload was not found.", source);
        }

        string target = ResolveTarget(relativeTarget);
        await RecordTargetAsync(relativeTarget, target, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }

    public async Task DeployTextAsync(
        string content,
        string relativeTarget,
        CancellationToken cancellationToken)
    {
        string target = ResolveTarget(relativeTarget);
        await RecordTargetAsync(relativeTarget, target, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, content, cancellationToken);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_manifestPath))
        {
            if (Directory.Exists(_backupRoot))
            {
                Directory.Delete(_backupRoot, true);
            }

            _manifest = null;
            return;
        }

        DeploymentManifest? manifest;
        await using (FileStream stream = File.OpenRead(_manifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync<DeploymentManifest>(
                stream,
                JsonOptions,
                cancellationToken);
        }

        foreach (DeploymentEntry entry in (manifest?.Entries ?? []).AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = ResolveTarget(entry.RelativeTarget);
            string backup = GetBackupPath(entry.RelativeTarget);
            if (entry.HadOriginal && File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(backup, target, true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        Directory.Delete(_backupRoot, true);
        _manifest = null;
    }

    private async Task RecordTargetAsync(
        string relativeTarget,
        string target,
        CancellationToken cancellationToken)
    {
        _manifest ??= new DeploymentManifest();
        if (_manifest.Entries.Any(item => string.Equals(
            item.RelativeTarget,
            relativeTarget,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
        {
            return;
        }

        bool hadOriginal = File.Exists(target);
        if (hadOriginal)
        {
            string backup = GetBackupPath(relativeTarget);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target, backup, true);
        }

        _manifest.Entries.Add(new DeploymentEntry
        {
            RelativeTarget = relativeTarget,
            HadOriginal = hadOriginal
        });
        // Record the recovery action before the target is overwritten.
        await SaveManifestAsync(cancellationToken);
    }

    private async Task SaveManifestAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupRoot);
        string temporary = _manifestPath + ".tmp";
        await using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, _manifest, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporary, _manifestPath, true);
    }

    private string ResolveTarget(string relativeTarget)
    {
        if (Path.IsPathRooted(relativeTarget))
        {
            throw new LauncherException("A deployment target must be relative to the game directory.");
        }

        string[] segments = relativeTarget.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new LauncherException($"Rejected unsafe deployment target '{relativeTarget}'.");
        }

        string target = Path.GetFullPath(Path.Combine(_gameRoot, relativeTarget));
        if (!PathSecurity.IsUnderRoot(_gameRoot, target)
            || PathSecurity.IsUnderRoot(_backupRoot, target))
        {
            throw new LauncherException($"Rejected unsafe deployment target '{relativeTarget}'.");
        }

        return target;
    }

    private string GetBackupPath(string relativeTarget) =>
        Path.GetFullPath(Path.Combine(_backupRoot, "original", relativeTarget));

    private sealed class DeploymentManifest
    {
        public bool Persistent { get; init; }
        public List<DeploymentEntry> Entries { get; init; } = [];
    }

    private sealed class DeploymentEntry
    {
        public string RelativeTarget { get; init; } = string.Empty;
        public bool HadOriginal { get; init; }
    }
}
