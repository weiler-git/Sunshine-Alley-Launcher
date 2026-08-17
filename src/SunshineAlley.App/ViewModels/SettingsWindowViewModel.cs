namespace SunshineAlley.App.ViewModels;

public sealed class SettingsWindowViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private string _gameDirectory;
    private string _modDataDirectory;
    private string _status = "Select the native Valheim install and a writable location for modpacks.";

    public SettingsWindowViewModel(MainWindowViewModel main)
    {
        _main = main;
        _gameDirectory = main.GameDirectory;
        _modDataDirectory = main.ModDataDirectory;
    }

    public string GameDirectory
    {
        get => _gameDirectory;
        set => SetField(ref _gameDirectory, value);
    }

    public string ModDataDirectory
    {
        get => _modDataDirectory;
        set => SetField(ref _modDataDirectory, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory)
            || string.IsNullOrWhiteSpace(ModDataDirectory))
        {
            Status = "Both directories are required.";
            return false;
        }

        await _main.UpdateDirectoriesAsync(GameDirectory, ModDataDirectory);
        Status = _main.Status;
        return string.Equals(Status, "Directories saved.", StringComparison.Ordinal);
    }
}
