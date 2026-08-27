using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SunshineAlley.App.ViewModels;

namespace SunshineAlley.App.Views;

public sealed partial class MainWindow : Window
{
    private const long CopyPointerActivationLifetimeMilliseconds = 2000;
    private static readonly TimeSpan CopyToastVisibleDuration = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan CopyToastFadeDuration = TimeSpan.FromMilliseconds(200);

    private bool _initialized;
    private Control? _copyPointerControl;
    private double _copyPointerWindowX;
    private long _copyPointerPressedAt;
    private CancellationTokenSource? _copyToastCancellation;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(
            InputElement.PointerPressedEvent,
            NoticeCopy_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
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
        if (sender is not Control control
            || control.DataContext is not NoticeItemViewModel noticeItem)
        {
            return;
        }

        double? pointerWindowX = ConsumeCopyPointerWindowX(control);
        if (!await CopyTextToClipboardAsync(noticeItem.CopyText))
        {
            return;
        }

        await ShowCopyToastAsync(control, pointerWindowX);
    }

    private void NoticeCopy_PointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.Source is not Visual source)
        {
            return;
        }

        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control control && control.Classes.Contains("notice-copy"))
            {
                _copyPointerControl = control;
                _copyPointerWindowX = eventArgs.GetPosition(this).X;
                _copyPointerPressedAt = Environment.TickCount64;
                return;
            }
        }
    }

    private double? ConsumeCopyPointerWindowX(Control control)
    {
        long pointerAge = Environment.TickCount64 - _copyPointerPressedAt;
        bool isCurrentPointerActivation = ReferenceEquals(_copyPointerControl, control)
            && pointerAge is >= 0 and <= CopyPointerActivationLifetimeMilliseconds;
        double pointerWindowX = _copyPointerWindowX;

        _copyPointerControl = null;
        _copyPointerPressedAt = 0;
        return isCurrentPointerActivation ? pointerWindowX : null;
    }

    private async Task<bool> CopyTextToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    private async Task ShowCopyToastAsync(Control target, double? pointerWindowX)
    {
        _copyToastCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _copyToastCancellation = cancellation;

        CopyToast.IsOpen = false;
        CopyToastBorder.Opacity = 1;
        PositionCopyToast(target, pointerWindowX);
        CopyToast.IsOpen = true;

        try
        {
            await Task.Delay(CopyToastVisibleDuration, cancellation.Token);
            if (!ReferenceEquals(_copyToastCancellation, cancellation))
            {
                return;
            }

            CopyToastBorder.Opacity = 0;
            await Task.Delay(CopyToastFadeDuration, cancellation.Token);
            if (!ReferenceEquals(_copyToastCancellation, cancellation))
            {
                return;
            }

            CopyToast.IsOpen = false;
            CopyToastBorder.Opacity = 1;
            _copyToastCancellation = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void PositionCopyToast(Control target, double? pointerWindowX)
    {
        CopyToast.PlacementTarget = target;
        CopyToastBorder.Measure(Size.Infinity);

        double toastWidth = CopyToastBorder.DesiredSize.Width;
        double centeredTargetOffset = (target.Bounds.Width - toastWidth) / 2;
        Point? targetPosition = target.TranslatePoint(default, this);
        double windowWidth = Bounds.Width;
        if (targetPosition is not { } targetTopLeft
            || toastWidth <= 0
            || windowWidth <= 0)
        {
            CopyToast.HorizontalOffset = centeredTargetOffset;
            return;
        }

        double desiredCenterWindowX = pointerWindowX
            ?? targetTopLeft.X + (target.Bounds.Width / 2);
        double maximumToastLeft = Math.Max(0, windowWidth - toastWidth);
        double safeToastLeft = Math.Clamp(
            desiredCenterWindowX - (toastWidth / 2),
            0,
            maximumToastLeft);
        CopyToast.HorizontalOffset = safeToastLeft - targetTopLeft.X;
    }
}
