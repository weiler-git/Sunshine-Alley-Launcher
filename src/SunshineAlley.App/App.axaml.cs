using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SunshineAlley.App.ViewModels;
using SunshineAlley.App.Views;
using SunshineAlley.Platform;
using SunshineAlley.Platform.Installation;

namespace SunshineAlley.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                ILauncherInstallationService installation =
                    LauncherInstallationServiceFactory.Create(
                        PlatformPaths.CreateDefault());
                InstallationInspection inspection = installation.Inspect();
                SetupIntent? forcedIntent = LauncherStartup.Current.SetupIntent;
                if (forcedIntent.HasValue
                    || (!LauncherStartup.Current.Portable
                        && !inspection.IsCurrentExecutable))
                {
                    var setupViewModel = new SetupWindowViewModel(
                        installation,
                        forcedIntent ?? SetupIntent.Install);
                    var setupWindow = new SetupWindow { DataContext = setupViewModel };
                    setupViewModel.ShutdownRequested += () => desktop.Shutdown();
                    desktop.MainWindow = setupWindow;
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
            }

            var viewModel = new MainWindowViewModel();
            var window = new MainWindow { DataContext = viewModel };
            viewModel.ShutdownRequested += () => desktop.Shutdown();
            desktop.MainWindow = window;
            desktop.Exit += async (_, _) => await viewModel.DisposeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
