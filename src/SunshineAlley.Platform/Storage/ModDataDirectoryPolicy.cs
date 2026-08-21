using System.Text;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Storage;

public sealed class ModDataDirectoryPolicy : IModDataDirectoryPolicy
{
    public const string MarkerFileName = ".sunshine-alley-root";
    internal const string MarkerContents = "Sunshine Alley Launcher managed mod-data root\nversion=1\n";

    private static readonly HashSet<string> AdoptableEntries = new(
        [
            MarkerFileName,
            LauncherConstants.ModPacksDirectoryName,
            "Launcher",
            "logs",
            "settings.json",
            ".DS_Store",
            "Thumbs.db",
            "desktop.ini"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly string _applicationDirectory;

    public ModDataDirectoryPolicy(string? applicationDirectory = null) =>
        _applicationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            applicationDirectory ?? AppContext.BaseDirectory));

    public async Task<DirectoryValidationResult> ValidateAsync(
        string candidate,
        string gameDirectory,
        bool initialize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return DirectoryValidationResult.Invalid(
                string.Empty,
                "Choose a dedicated directory for Sunshine Alley mod data.");
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return DirectoryValidationResult.Invalid(
                candidate,
                "The mod-data directory path is invalid: " + exception.Message);
        }

        string? broadPathProblem = FindBroadPathProblem(normalized);
        if (broadPathProblem is not null)
        {
            return DirectoryValidationResult.Invalid(normalized, broadPathProblem);
        }

        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            string normalizedGame;
            try
            {
                normalizedGame = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(gameDirectory));
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return DirectoryValidationResult.Invalid(
                    normalized,
                    "The selected game-directory path is invalid: " + exception.Message);
            }
            if (PathSecurity.IsUnderRoot(normalizedGame, normalized)
                || PathSecurity.IsUnderRoot(normalized, normalizedGame))
            {
                return DirectoryValidationResult.Invalid(
                    normalized,
                    "The mod-data directory must not be the Valheim directory, inside it, or one of its parent directories.");
            }
        }

        if (Directory.Exists(normalized))
        {
            if (PathSecurity.ContainsReparsePoint(normalized))
            {
                return DirectoryValidationResult.Invalid(
                    normalized,
                    "The mod-data directory cannot be a symbolic link or Windows junction.");
            }

            string marker = Path.Combine(normalized, MarkerFileName);
            if (File.Exists(marker))
            {
                if (PathSecurity.ContainsReparsePoint(marker))
                {
                    return DirectoryValidationResult.Invalid(
                        normalized,
                        "The launcher ownership marker cannot be a symbolic link or Windows reparse point.");
                }

                string contents = await File.ReadAllTextAsync(marker, cancellationToken);
                if (!string.Equals(contents, MarkerContents, StringComparison.Ordinal))
                {
                    return DirectoryValidationResult.Invalid(
                        normalized,
                        $"The existing {MarkerFileName} marker is not recognized.");
                }

                return DirectoryValidationResult.Valid(normalized);
            }

            string[] unknownEntries = Directory
                .EnumerateFileSystemEntries(normalized, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !AdoptableEntries.Contains(Path.GetFileName(path)))
                .Take(3)
                .Select(Path.GetFileName)
                .ToArray();
            if (unknownEntries.Length > 0)
            {
                return DirectoryValidationResult.Invalid(
                    normalized,
                    "Choose an empty directory or an existing Sunshine Alley data directory. "
                    + "This directory contains unrelated items: "
                    + string.Join(", ", unknownEntries));
            }
        }

        if (!initialize)
        {
            return DirectoryValidationResult.Valid(normalized);
        }

        try
        {
            Directory.CreateDirectory(normalized);
            if (PathSecurity.ContainsReparsePoint(normalized))
            {
                return DirectoryValidationResult.Invalid(
                    normalized,
                    "The mod-data directory resolved to a symbolic link or Windows junction.");
            }

            await WriteMarkerAsync(normalized, cancellationToken);
            return DirectoryValidationResult.Valid(normalized);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return DirectoryValidationResult.Invalid(
                normalized,
                "The launcher cannot initialize this mod-data directory: " + exception.Message);
        }
    }

    private string? FindBroadPathProblem(string candidate)
    {
        string? root = Path.GetPathRoot(candidate);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(candidate, root))
        {
            return "A filesystem root cannot be used as the mod-data directory.";
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile)
            && (PathsEqual(candidate, userProfile)
                || PathSecurity.IsUnderRoot(candidate, userProfile)))
        {
            return "The user-profile directory is too broad. Choose a dedicated Sunshine Alley subdirectory.";
        }

        foreach ((string Path, string Name) protectedPath in GetProtectedUserDirectories(userProfile))
        {
            if (PathSecurity.IsUnderRoot(protectedPath.Path, candidate)
                || PathSecurity.IsUnderRoot(candidate, protectedPath.Path))
            {
                return $"The mod-data directory cannot be {protectedPath.Name} or a directory inside it.";
            }
        }

        if (PathSecurity.IsUnderRoot(_applicationDirectory, candidate)
            || PathSecurity.IsUnderRoot(candidate, _applicationDirectory))
        {
            return "The launcher installation directory cannot be used for mod data.";
        }

        return null;
    }

    private static IEnumerable<(string Path, string Name)> GetProtectedUserDirectories(
        string userProfile)
    {
        var values = new List<(string Path, string Name)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents"),
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop"),
            (Path.Combine(userProfile, "Downloads"), "Downloads"),
            (Path.GetTempPath(), "the temporary-files directory")
        };

        return values.Where(value =>
            !string.IsNullOrWhiteSpace(value.Path)
            && !PathsEqual(value.Path, userProfile));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static async Task WriteMarkerAsync(
        string root,
        CancellationToken cancellationToken)
    {
        string marker = Path.Combine(root, MarkerFileName);
        if (File.Exists(marker))
        {
            return;
        }

        string temporary = Path.Combine(
            root,
            $".{MarkerFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                MarkerContents,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, marker, false);
        }
        catch (IOException) when (File.Exists(marker))
        {
            // Another launcher process initialized the same root first.
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
