using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SunshineAlley.App.ViewModels;

namespace SunshineAlley.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        Opened += MainWindow_Opened;
    }

    private async void MainWindow_Opened(object? sender, EventArgs eventArgs)
    {
        if (_initialized || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        await viewModel.InitializeAsync();
    }

    private async void OptionalMods_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !viewModel.CanManageOptionalMods)
        {
            return;
        }

        var window = new OptionalModsWindow(viewModel);
        await window.ShowDialog(this);
        await viewModel.ReverifySelectedAsync();
    }

    private async void Settings_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var window = new SettingsWindow(viewModel);
        await window.ShowDialog(this);
    }

    private async void OpenData_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.OpenModDataDirectoryAsync();
        }
    }

    private async void NoticeCopy_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { DataContext: NoticeItemViewModel noticeItem })
        {
            await CopyTextToClipboardAsync(noticeItem.CopyText);
        }
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
