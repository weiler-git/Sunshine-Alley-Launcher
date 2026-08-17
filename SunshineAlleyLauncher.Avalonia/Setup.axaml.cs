using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;

namespace SunshineAlleyLauncher.Avalonia;

public partial class Setup : Window

{

    private enum Mode
{
    Install,
    Repair,
    Uninstall,
    Shortcuts
}

private Mode mode = Mode.Install;
private const double MarginX = 5;
private const double MarginY = 5;

private double defaultInstallX;
private double defaultInstallY;
private double defaultRepairX;
private double defaultUninstallX;
private double defaultShortcutsX;

private double installHeight;
private double repairHeight;
private double uninstallHeight;
private double shortcutsHeight;
private double startMenuHeight;
private double desktopHeight;
private double installLabelHeight;
private double gameLabelHeight;
private double widestDirectoryLabel;
private const bool IsInstalledForGuiTest = false;

    public Setup()
    {
        InitializeComponent();
        radioButtonInstall.IsCheckedChanged += radioButtonInstall_CheckedChanged;
        radioButtonRepair.IsCheckedChanged += radioButtonRepair_CheckedChanged;
        radioButtonUninstall.IsCheckedChanged += radioButtonUninstall_CheckedChanged;
        radioButtonShortcuts.IsCheckedChanged += radioButtonShortcuts_CheckedChanged;
        Opened += Setup_Opened;
    }

    private void radioButtonInstall_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (radioButtonInstall.IsChecked == true)
        {
            mode = Mode.Install;
            SetMode();
        }
    }

    private void radioButtonRepair_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (radioButtonRepair.IsChecked == true)
        {
            mode = Mode.Repair;
            SetMode();
        }
    }

    private void radioButtonUninstall_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (radioButtonUninstall.IsChecked == true)
        {
            mode = Mode.Uninstall;
            SetMode();
        }
    }

    private void radioButtonShortcuts_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (radioButtonShortcuts.IsChecked == true)
        {
            mode = Mode.Shortcuts;
            SetMode();
        }
    }

    private void SetMode()
    {
        switch (mode)
        {
            case Mode.Install:
                PositionInstallMode();

                labelInstallDirectory.IsVisible = true;
                labelInstallDirectoryPath.IsVisible = true;
                labelGameDirectory.IsVisible = true;
                labelGameDirectoryPath.IsVisible = true;
                checkBoxShortcutStartMenu.IsVisible = true;
                checkBoxShortcutDesktop.IsVisible = true;

                SetBackground("vikingarms.jpg");
                buttonNext.Content = "Install";
                break;

            case Mode.Repair:
                PositionNormalMode();

                labelInstallDirectory.IsVisible = false;
                labelInstallDirectoryPath.IsVisible = false;
                labelGameDirectory.IsVisible = false;
                labelGameDirectoryPath.IsVisible = false;
                checkBoxShortcutStartMenu.IsVisible = false;
                checkBoxShortcutDesktop.IsVisible = false;

                SetBackground("vikingarms.jpg");
                buttonNext.Content = "Repair";
                break;

            case Mode.Uninstall:
                PositionNormalMode();

                labelInstallDirectory.IsVisible = false;
                labelInstallDirectoryPath.IsVisible = false;
                labelGameDirectory.IsVisible = false;
                labelGameDirectoryPath.IsVisible = false;
                checkBoxShortcutStartMenu.IsVisible = false;
                checkBoxShortcutDesktop.IsVisible = false;

                SetBackground("3646288.jpg");
                buttonNext.Content = "Uninstall";
                break;

            case Mode.Shortcuts:
                PositionNormalMode();

                labelInstallDirectory.IsVisible = false;
                labelInstallDirectoryPath.IsVisible = false;
                labelGameDirectory.IsVisible = false;
                labelGameDirectoryPath.IsVisible = false;

                double startMenuY =
                    Canvas.GetTop(radioButtonShortcuts) + shortcutsHeight + MarginY;

                Canvas.SetLeft(
                    checkBoxShortcutStartMenu,
                    defaultInstallX + MarginX);

                Canvas.SetTop(
                    checkBoxShortcutStartMenu,
                    startMenuY);

                double desktopY =
                    startMenuY + startMenuHeight + MarginY;

                Canvas.SetLeft(
                    checkBoxShortcutDesktop,
                    defaultInstallX + MarginX);

                Canvas.SetTop(
                    checkBoxShortcutDesktop,
                    desktopY);

                checkBoxShortcutStartMenu.IsVisible = true;
                checkBoxShortcutDesktop.IsVisible = true;

                SetBackground("vikingarms.jpg");
                buttonNext.Content = "Create";
                break;
        }
    }
    
    private void SetBackground(string fileName)
    {
        using var stream = AssetLoader.Open(
            new Uri($"avares://SunshineAlleyLauncher.Avalonia/res/{fileName}"));

        backgroundImage.Source = new Bitmap(stream);
    }

    private void Setup_Opened(object? sender, EventArgs e)
    {
        defaultInstallX = Canvas.GetLeft(radioButtonInstall);
        defaultInstallY = Canvas.GetTop(radioButtonInstall);
        defaultRepairX = Canvas.GetLeft(radioButtonRepair);
        defaultUninstallX = Canvas.GetLeft(radioButtonUninstall);
        defaultShortcutsX = Canvas.GetLeft(radioButtonShortcuts);

        installHeight = radioButtonInstall.Bounds.Height;
        repairHeight = radioButtonRepair.Bounds.Height;
        uninstallHeight = radioButtonUninstall.Bounds.Height;
        shortcutsHeight = radioButtonShortcuts.Bounds.Height;

        startMenuHeight = checkBoxShortcutStartMenu.Bounds.Height;
        desktopHeight = checkBoxShortcutDesktop.Bounds.Height;

        installLabelHeight = labelInstallDirectory.Bounds.Height;
        gameLabelHeight = labelGameDirectory.Bounds.Height;

        widestDirectoryLabel = Math.Max(
            labelGameDirectory.Bounds.Width,
            labelInstallDirectory.Bounds.Width);

        progressBar1.IsVisible = false;

        if (IsInstalledForGuiTest)
        {
            radioButtonInstall.IsEnabled = false;
            radioButtonInstall.IsVisible = false;

            radioButtonRepair.IsEnabled = true;
            radioButtonRepair.IsVisible = true;
            radioButtonUninstall.IsEnabled = true;
            radioButtonUninstall.IsVisible = true;
            radioButtonShortcuts.IsEnabled = true;
            radioButtonShortcuts.IsVisible = true;

            radioButtonRepair.IsChecked = true;
        }
        else
        {
            radioButtonInstall.IsEnabled = true;
            radioButtonInstall.IsVisible = true;

            radioButtonRepair.IsEnabled = false;
            radioButtonRepair.IsVisible = false;
            radioButtonUninstall.IsEnabled = false;
            radioButtonUninstall.IsVisible = false;
            radioButtonShortcuts.IsEnabled = false;
            radioButtonShortcuts.IsVisible = false;

            radioButtonInstall.IsChecked = true;
        }
    }

    private void PositionInstallMode()
    {
        double innerX = defaultInstallX + MarginX;

        Canvas.SetLeft(labelInstallDirectory, innerX);
        Canvas.SetLeft(labelGameDirectory, innerX);

        Canvas.SetLeft(labelInstallDirectoryPath,
            innerX + widestDirectoryLabel + MarginX);

        Canvas.SetLeft(labelGameDirectoryPath,
            innerX + widestDirectoryLabel + MarginX);

        double installDirectoryY =
            defaultInstallY + installHeight + MarginY;

        Canvas.SetTop(labelInstallDirectory, installDirectoryY);
        Canvas.SetTop(labelInstallDirectoryPath, installDirectoryY);

        double gameDirectoryY =
            installDirectoryY + installLabelHeight + MarginY;

        Canvas.SetTop(labelGameDirectory, gameDirectoryY);
        Canvas.SetTop(labelGameDirectoryPath, gameDirectoryY);

        double startMenuY =
            gameDirectoryY + gameLabelHeight + MarginY;

        Canvas.SetLeft(checkBoxShortcutStartMenu, innerX);
        Canvas.SetTop(checkBoxShortcutStartMenu, startMenuY);

        double desktopY =
            startMenuY + startMenuHeight + MarginY;

        Canvas.SetLeft(checkBoxShortcutDesktop, innerX);
        Canvas.SetTop(checkBoxShortcutDesktop, desktopY);

        double repairY =
            desktopY + desktopHeight + MarginY;

        Canvas.SetLeft(radioButtonRepair, defaultRepairX);
        Canvas.SetTop(radioButtonRepair, repairY);

        double uninstallY =
            repairY + repairHeight + MarginY;

        Canvas.SetLeft(radioButtonUninstall, defaultUninstallX);
        Canvas.SetTop(radioButtonUninstall, uninstallY);

        double shortcutsY =
            uninstallY + uninstallHeight + MarginY;

        Canvas.SetLeft(radioButtonShortcuts, defaultShortcutsX);
        Canvas.SetTop(radioButtonShortcuts, shortcutsY);
    }
    
    private void PositionNormalMode()
    {
        double repairY =
            defaultInstallY + installHeight + MarginY;

        Canvas.SetLeft(radioButtonRepair, defaultRepairX);
        Canvas.SetTop(radioButtonRepair, repairY);

        double uninstallY =
            repairY + repairHeight + MarginY;

        Canvas.SetLeft(radioButtonUninstall, defaultUninstallX);
        Canvas.SetTop(radioButtonUninstall, uninstallY);

        double shortcutsY =
            uninstallY + uninstallHeight + MarginY;

        Canvas.SetLeft(radioButtonShortcuts, defaultShortcutsX);
        Canvas.SetTop(radioButtonShortcuts, shortcutsY);
    }
    
}