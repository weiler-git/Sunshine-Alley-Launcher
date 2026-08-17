using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SunshineAlley.App.ViewModels;

namespace SunshineAlley.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(MainWindowViewModel main)
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel(main);
    }

    private async void BrowseGame_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            string? selected = await PickFolderAsync("Select the Valheim game directory", viewModel.GameDirectory);
            if (selected is not null)
            {
                viewModel.GameDirectory = selected;
                await viewModel.ValidateGameDirectoryAsync();
            }
        }
    }

    private async void BrowseData_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            string? selected = await PickFolderAsync(
                "Select the mod data directory",
                viewModel.ModDataBrowseStart);
            if (selected is not null)
            {
                viewModel.ModDataDirectory = selected;
                await viewModel.ValidateModDataDirectoryAsync();
            }
        }
    }

    private async void GameDirectory_LostFocus(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            await viewModel.ValidateGameDirectoryAsync();
        }
    }

    private async void ModDataDirectory_LostFocus(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            await viewModel.ValidateModDataDirectoryAsync();
        }
    }

    private async Task<string?> PickFolderAsync(string title, string current)
    {
        IStorageFolder? suggested = null;
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
        {
            var builder = new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Path = Path.GetFullPath(current)
            };
            suggested = await StorageProvider.TryGetFolderFromPathAsync(builder.Uri);
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggested
            });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async void Save_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SettingsWindowViewModel viewModel && await viewModel.SaveAsync())
        {
            Close();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs) => Close();
}
