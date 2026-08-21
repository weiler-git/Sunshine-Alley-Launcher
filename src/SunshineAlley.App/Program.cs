using Avalonia;
using SunshineAlley.Platform.Installation;
using SunshineAlley.Platform.Update;

namespace SunshineAlley.App;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        int? updateResult = (OperatingSystem.IsLinux()
                ? LinuxUpdateHelper.TryRunAsync(args)
                : WindowsUpdateHelper.TryRunAsync(args))
            .GetAwaiter()
            .GetResult();
        if (updateResult.HasValue)
        {
            Environment.ExitCode = updateResult.Value;
            return;
        }

        int? maintenanceResult = (OperatingSystem.IsLinux()
                ? LinuxMaintenanceHelper.TryRunAsync(args)
                : WindowsMaintenanceHelper.TryRunAsync(args))
            .GetAwaiter()
            .GetResult();
        if (maintenanceResult.HasValue)
        {
            Environment.ExitCode = maintenanceResult.Value;
            return;
        }

#if SUNSHINE_ALLOW_UNSIGNED_UPDATES
        if (args.Contains(
                "--simulate-update-startup-failure",
                StringComparer.OrdinalIgnoreCase)
            && args.Contains("--post-update", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = 86;
            return;
        }
#endif

        LauncherStartup.PrepareProcessAsync(args).GetAwaiter().GetResult();
        if (OperatingSystem.IsWindows())
        {
            _singleInstance = new Mutex(
                true,
                "Local\\SunshineAlleyLauncher.V3",
                out bool createdNew);
            if (!createdNew)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                return;
            }
        }

        try
        {
            LauncherStartup.Initialize(args);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
            _singleInstance = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
