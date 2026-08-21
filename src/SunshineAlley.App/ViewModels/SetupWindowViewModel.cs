using SunshineAlley.Platform.Installation;
using SunshineAlley.Platform.Steam;

namespace SunshineAlley.App.ViewModels;

public enum SetupIntent
{
    Install,
    Repair,
    Uninstall
}

public sealed class SetupWindowViewModel : ViewModelBase
{
    private readonly ILauncherInstallationService _installation;
    private string _applicationDirectory;
    private string _dataDirectory;
    private string _gameDirectory = string.Empty;
    private string _status;
    private bool _isBusy;
    private bool _createStartMenuShortcut = true;
    private bool _createDesktopShortcut;
    private bool _resetDownloadedData;
    private bool _removeDataOnUninstall;
    private bool _removeConfigurationOnUninstall;
    private bool _confirmUninstall;

    public SetupWindowViewModel(
        ILauncherInstallationService installation,
        SetupIntent intent)
    {
        _installation = installation;
        Intent = intent;
        Inspection = installation.Inspect();
        _applicationDirectory = Inspection.RegisteredExecutable is null
            ? installation.Paths.ApplicationDirectory
            : Path.GetDirectoryName(Inspection.RegisteredExecutable)!;
        _dataDirectory = installation.ReadConfiguredDataDirectory()
            ?? installation.Paths.DataDirectory;
        _gameDirectory = installation.ReadConfiguredGameDirectory() ?? string.Empty;
        _status = Inspection.Message;
    }

    public event Action? ShutdownRequested;

    public SetupIntent Intent { get; }
    public InstallationInspection Inspection { get; }
    public bool IsInstall => Intent == SetupIntent.Install && !Inspection.IsInstalled;
    public bool IsRepair => Intent == SetupIntent.Repair
        || (Intent == SetupIntent.Install && Inspection.IsInstalled);
    public bool IsUninstall => Intent == SetupIntent.Uninstall;
    public bool ShowsInstallOptions => !IsUninstall;
    public bool ShowsApplicationDirectory => _installation.CanChooseApplicationDirectory;
    public bool ShowsFixedApplicationDirectoryNote =>
        !_installation.CanChooseApplicationDirectory;
    public bool ShowsWindowsShortcutOptions => _installation.ShowsWindowsShortcutOptions;
    public bool IsLegacyMigration => Inspection.IsLegacyLaunch;
    public bool CanChangeApplicationDirectory =>
        _installation.CanChooseApplicationDirectory && !Inspection.IsInstalled;
    public string FixedApplicationDirectoryNote =>
        $"Launcher files will be installed for this user at {ApplicationDirectory}.";
    public string UninstallDescription => _installation.UninstallDescription;
    public string Heading => IsUninstall
        ? "Uninstall Sunshine Alley"
        : IsRepair
            ? "Repair Sunshine Alley"
            : IsLegacyMigration
                ? "Migrate Sunshine Alley Launcher"
                : "Install Sunshine Alley Launcher";
    public string PrimaryButtonText => IsUninstall
        ? "Uninstall"
        : IsRepair
            ? "Repair installation"
            : IsLegacyMigration
                ? "Migrate and install"
                : "Install";

    public string ApplicationDirectory
    {
        get => _applicationDirectory;
        set => SetField(ref _applicationDirectory, value);
    }

    public string DataDirectory
    {
        get => _dataDirectory;
        set => SetField(ref _dataDirectory, value);
    }

    public string GameDirectory
    {
        get => _gameDirectory;
        set => SetField(ref _gameDirectory, value);
    }

    public bool CreateStartMenuShortcut
    {
        get => _createStartMenuShortcut;
        set => SetField(ref _createStartMenuShortcut, value);
    }

    public bool CreateDesktopShortcut
    {
        get => _createDesktopShortcut;
        set => SetField(ref _createDesktopShortcut, value);
    }

    public bool ResetDownloadedData
    {
        get => _resetDownloadedData;
        set => SetField(ref _resetDownloadedData, value);
    }

    public bool RemoveDataOnUninstall
    {
        get => _removeDataOnUninstall;
        set => SetField(ref _removeDataOnUninstall, value);
    }

    public bool RemoveConfigurationOnUninstall
    {
        get => _removeConfigurationOnUninstall;
        set => SetField(ref _removeConfigurationOnUninstall, value);
    }

    public bool ConfirmUninstall
    {
        get => _confirmUninstall;
        set => SetField(ref _confirmUninstall, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public async Task InitializeAsync()
    {
        if (IsUninstall || !string.IsNullOrWhiteSpace(GameDirectory))
        {
            return;
        }

        try
        {
            var steam = new SteamService();
            GameDirectory = (await steam.DiscoverAsync())?.GameDirectory ?? string.Empty;
        }
        catch
        {
            // The player can select the directory manually.
        }
    }

    public async Task ExecuteAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsUninstall)
            {
                if (!ConfirmUninstall)
                {
                    Status = "Confirm that you want to uninstall before continuing.";
                    return;
                }

                _installation.CreateMaintenanceHelper(
                    MaintenanceOperation.Uninstall,
                    RemoveDataOnUninstall,
                    RemoveConfigurationOnUninstall);
                Status = "The launcher will be removed after this window closes.";
                ShutdownRequested?.Invoke();
                return;
            }

            var request = new InstallationRequest(
                ApplicationDirectory,
                DataDirectory,
                GameDirectory,
                CreateStartMenuShortcut,
                CreateDesktopShortcut,
                ResetDownloadedData);
            InstallationResult result = await _installation.InstallOrRepairAsync(request);
            Status = result.Message + " Starting the installed copy…";
            _installation.StartInstalledCopy(
                result.InstalledExecutable,
                result.LegacyFilesToRemove);
            ShutdownRequested?.Invoke();
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void StartRegisteredInstallation()
    {
        if (Inspection.RegisteredExecutable is null)
        {
            return;
        }

        _installation.StartInstalledCopy(Inspection.RegisteredExecutable, []);
        ShutdownRequested?.Invoke();
    }
}
