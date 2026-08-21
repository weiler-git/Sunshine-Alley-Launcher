using Avalonia.Controls;
using Avalonia.Interactivity;
using SunshineAlley.App.ViewModels;

namespace SunshineAlley.App.Views;

public sealed partial class OptionalModsWindow : Window
{
    public OptionalModsWindow(MainWindowViewModel main)
    {
        InitializeComponent();
        DataContext = new OptionalModsWindowViewModel(main);
    }

    private void Done_Click(object? sender, RoutedEventArgs eventArgs) => Close();
}
