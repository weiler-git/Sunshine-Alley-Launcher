using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SunshineAlley.App.ViewModels;

namespace SunshineAlley.App.Views;

public sealed partial class SetupWindow : Window
{
    private bool _initialized;

    public SetupWindow()
    {
        InitializeComponent();
        Opened += SetupWindow_Opened;
    }

    private async void SetupWindow_Opened(object? sender, EventArgs eventArgs)
    {
        if (_initialized || DataContext is not SetupWindowViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        await viewModel.InitializeAsync();
    }

    private async void BrowseApp_Click(object? sender, RoutedEventArgs eventArgs) =>
        await BrowseAsync(value => value.ApplicationDirectory, (value, path) => value.ApplicationDirectory = path);

    private async void BrowseData_Click(object? sender, RoutedEventArgs eventArgs) =>
        await BrowseAsync(value => value.DataDirectory, (value, path) => value.DataDirectory = path);

    private async void BrowseGame_Click(object? sender, RoutedEventArgs eventArgs) =>
        await BrowseAsync(value => value.GameDirectory, (value, path) => value.GameDirectory = path);

    private async Task BrowseAsync(
        Func<SetupWindowViewModel, string> currentSelector,
        Action<SetupWindowViewModel, string> setter)
    {
        if (DataContext is not SetupWindowViewModel viewModel)
        {
            return;
        }

        IStorageFolder? suggested = null;
        string current = currentSelector(viewModel);
        string? existing = FindExistingDirectory(current);
        if (existing is not null)
        {
            var builder = new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Path = existing
            };
            suggested = await StorageProvider.TryGetFolderFromPathAsync(builder.Uri);
        }

        IReadOnlyList<IStorageFolder> selected = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select a directory",
                AllowMultiple = false,
                SuggestedStartLocation = suggested
            });
        if (selected.Count > 0)
        {
            setter(viewModel, selected[0].Path.LocalPath);
        }
    }

    private static string? FindExistingDirectory(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            DirectoryInfo? directory = new(Path.GetFullPath(candidate));
            while (directory is not null && !directory.Exists)
            {
                directory = directory.Parent;
            }

            return directory?.FullName;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private async void Execute_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SetupWindowViewModel viewModel)
        {
            await viewModel.ExecuteAsync();
        }
    }

    private void RunInstalled_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is SetupWindowViewModel viewModel)
        {
            viewModel.StartRegisteredInstallation();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs) => Close();
}
