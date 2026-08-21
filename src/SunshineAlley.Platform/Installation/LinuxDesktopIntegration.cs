using System.Text;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Installation;

internal static class LinuxDesktopIntegration
{
    private const string IconResource = "SunshineAlley.LinuxIcon.png";

    public static async Task InstallAsync(
        PlatformPaths paths,
        string executable,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Freedesktop application integration requires Linux.");
        }

        string desktopEntry = paths.LinuxDesktopEntryPath;
        string icon = paths.LinuxIconPath;
        EnsureFinalTargetIsNotLink(desktopEntry);
        EnsureFinalTargetIsNotLink(icon);

        using Stream iconStream = typeof(LinuxDesktopIntegration).Assembly
            .GetManifestResourceStream(IconResource)
            ?? throw new LauncherException(
                $"Embedded Linux application icon '{IconResource}' was not found.");
        await WriteStreamAtomicAsync(icon, iconStream, cancellationToken);

        string contents = CreateDesktopEntry(executable, icon);
        await WriteTextAtomicAsync(desktopEntry, contents, cancellationToken);
    }

    public static void Remove(PlatformPaths paths)
    {
        DeleteFile(paths.LinuxDesktopEntryPath);
        DeleteFile(paths.LinuxIconPath);
    }

    internal static string CreateDesktopEntry(string executable, string icon)
    {
        executable = ValidateAbsolutePath(executable, nameof(executable));
        icon = ValidateAbsolutePath(icon, nameof(icon));
        return string.Join(
            "\n",
            new[]
            {
                "[Desktop Entry]",
                "Version=1.0",
                "Type=Application",
                "Name=Sunshine Alley Launcher",
                "Comment=Launch Valheim with Sunshine Alley modpacks",
                $"Exec={QuoteExecArgument(executable)}",
                $"Icon={EscapeDesktopString(icon)}",
                "Terminal=false",
                "Categories=Game;",
                "StartupNotify=true",
                "StartupWMClass=SunshineAlleyLauncher",
                string.Empty
            });
    }

    private static string ValidateAbsolutePath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Path.IsPathFullyQualified(value)
            || value.Any(character => character is '\0' or '\r' or '\n'))
        {
            throw new ArgumentException(
                "Desktop integration paths must be absolute single-line paths.",
                parameterName);
        }

        return Path.GetFullPath(value);
    }

    private static string QuoteExecArgument(string value) =>
        "\""
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal)
        + "\"";

    private static string EscapeDesktopString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    private static async Task WriteTextAtomicAsync(
        string destination,
        string contents,
        CancellationToken cancellationToken)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
        await using var input = new MemoryStream(bytes, writable: false);
        await WriteStreamAtomicAsync(destination, input, cancellationToken);
    }

    private static async Task WriteStreamAtomicAsync(
        string destination,
        Stream input,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.SetUnixFileMode(
                temporary,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void EnsureFinalTargetIsNotLink(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new LauncherException(
                $"Desktop integration target is a symbolic link and was not replaced: '{path}'.");
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
