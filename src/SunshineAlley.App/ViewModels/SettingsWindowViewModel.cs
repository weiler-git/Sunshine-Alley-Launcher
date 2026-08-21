using SunshineAlley.Core;

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

    public string ModDataBrowseStart => Directory.Exists(ModDataDirectory)
        ? ModDataDirectory
        : _main.DefaultModDataDirectory;

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

        DirectoryValidationResult gameValidation = await _main.ValidateGameDirectoryAsync(
            GameDirectory);
        if (!gameValidation.IsValid)
        {
            Status = gameValidation.Error!;
            return false;
        }

        DirectoryValidationResult dataValidation =
            await _main.ValidateModDataDirectoryAsync(
                ModDataDirectory,
                gameValidation.NormalizedPath);
        if (!dataValidation.IsValid)
        {
            Status = dataValidation.Error!;
            return false;
        }

        GameDirectory = gameValidation.NormalizedPath;
        ModDataDirectory = dataValidation.NormalizedPath;
        try
        {
            await _main.UpdateDirectoriesAsync(GameDirectory, ModDataDirectory);
            Status = "Directories saved.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public async Task ValidateGameDirectoryAsync()
    {
        DirectoryValidationResult result = await _main.ValidateGameDirectoryAsync(
            GameDirectory);
        Status = result.IsValid
            ? "Valheim game directory found."
            : result.Error!;
        if (result.IsValid)
        {
            GameDirectory = result.NormalizedPath;
        }
    }

    public async Task ValidateModDataDirectoryAsync()
    {
        DirectoryValidationResult result =
            await _main.ValidateModDataDirectoryAsync(
                ModDataDirectory,
                GameDirectory);
        Status = result.IsValid
            ? "Mod-data directory is safe to use."
            : result.Error!;
        if (result.IsValid)
        {
            ModDataDirectory = result.NormalizedPath;
        }
    }
}
