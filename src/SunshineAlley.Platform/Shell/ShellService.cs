using System.Diagnostics;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Shell;

public sealed class ShellService : IShellService
{
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        if (OperatingSystem.IsWindows())
        {
            Start("explorer.exe", [path]);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Start("/usr/bin/open", [path]);
        }
        else
        {
            Start("xdg-open", [path]);
        }

        return Task.CompletedTask;
    }

    public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException($"Refusing to open unsupported URI scheme '{uri.Scheme}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        };
        Process.Start(startInfo)?.Dispose();
        return Task.CompletedTask;
    }

    private static void Start(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo)?.Dispose();
    }
}
