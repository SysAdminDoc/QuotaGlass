using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;
using QuotaGlass.Widget.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace QuotaGlass.Widget.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIconService _tray;
    private TopMostEnforcer? _topMost;

    public MainWindow()
    {
        InitializeComponent();

        // Wire alarms — toast + fire-once store + scheduler. The scheduler is
        // injected into MainViewModel so it gets every snapshot the widget
        // sees, with no separate file-watcher pipeline.
        var toast = new ToastService();
        var firedStore = new FiredRulesStore();
        var alarms = new AlarmScheduler(Dispatcher, toast, firedStore);

        _vm = new MainViewModel(Dispatcher, alarms);
        DataContext = _vm;

        _tray = new TrayIconService();
        _tray.ShowRequested += (_, _) => { Show(); Activate(); _tray.OnVisibilityChanged(true); };
        _tray.HideRequested += (_, _) => { Hide(); _tray.OnVisibilityChanged(false); };
        _tray.RefreshRequested += (_, _) => { /* TODO N-15 settings: bump refresh now */ };
        _tray.SettingsRequested += (_, _) => { Show(); Activate(); _vm.Settings.IsExpanded = true; };
        _tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync();
        _tray.ResetPositionRequested += (_, _) => ResetWidgetPosition();
        _tray.QuitRequested += (_, _) => System.Windows.Application.Current.Shutdown();

        _vm.Buckets.CollectionChanged += (_, _) => RefreshTrayBadge();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Buckets)) RefreshTrayBadge();
        };

        Loaded += (_, _) =>
        {
            _vm.Start();
            // R3-P1-06 — gate the balloon to first-run only. Set the flag
            // before showing the balloon so a crash mid-show doesn't replay.
            if (!_vm.SettingsStore.Current.Widget.HasShownFirstRunToast)
            {
                _vm.SettingsStore.Update(s => s.Widget.HasShownFirstRunToast = true);
                _tray.NotifyFirstRun();
            }
            _tray.OnVisibilityChanged(IsVisible);
        };
        Closed += (_, _) =>
        {
            _topMost?.Dispose();
            _tray.Dispose();
            _vm.Dispose();
        };
        IsVisibleChanged += (_, _) => _tray.OnVisibilityChanged(IsVisible);
    }

    private void RefreshTrayBadge()
    {
        double worst = 0;
        foreach (var b in _vm.Buckets)
        {
            if (b.Percent > worst) worst = b.Percent;
        }
        _tray.UpdateBadge(worst);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Re-assert TOPMOST on every foreground change. UAC dialogs and
        // fullscreen apps would otherwise demote us.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _topMost = new TopMostEnforcer(hwnd);
        }

        // F-N5: enable Win11 Mica system backdrop. No-op on Win10.
        MicaBackdrop.TryApply(this, acrylic: false);

        // NX-05: restore last-known position if it's still on-screen.
        var pos = _vm.SettingsStore.Current.Widget;
        if (pos.X is double x && pos.Y is double y && IsPointOnScreen(x, y))
        {
            Left = x;
            Top = y;
        }

        // Persist position on every move + on close.
        LocationChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _vm.SettingsStore.Update(s => { s.Widget.X = Left; s.Widget.Y = Top; });
            }
        };
    }

    private static bool IsPointOnScreen(double x, double y)
    {
        // Slop of 32 px ensures a slightly-off-screen widget still finds
        // its monitor. Anything wildly off-screen (display unplugged)
        // falls back to default placement.
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var r = screen.WorkingArea;
            if (x + 32 >= r.Left && x - 32 <= r.Right
                && y + 32 >= r.Top && y - 32 <= r.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    private void OnChromeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            SnapToMonitorEdge();
        }
    }

    /// <summary>
    /// NX-04 — after a drag, if the window's left/top/right/bottom landed
    /// within 16 px of the current monitor's working area, snap to that edge.
    /// Multi-monitor aware via WPF's Screen API.
    /// </summary>
    private void SnapToMonitorEdge()
    {
        const double snapThreshold = 16.0;
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)(Left + ActualWidth / 2), (int)(Top + ActualHeight / 2)));
            var area = screen.WorkingArea;

            // Convert device pixels to DIPs via current DPI scaling.
            var source = PresentationSource.FromVisual(this);
            double dpiX = 1.0, dpiY = 1.0;
            if (source?.CompositionTarget is not null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            var areaLeft = area.Left / dpiX;
            var areaTop = area.Top / dpiY;
            var areaRight = area.Right / dpiX;
            var areaBottom = area.Bottom / dpiY;

            if (Math.Abs(Left - areaLeft) <= snapThreshold) Left = areaLeft;
            else if (Math.Abs((Left + ActualWidth) - areaRight) <= snapThreshold) Left = areaRight - ActualWidth;

            if (Math.Abs(Top - areaTop) <= snapThreshold) Top = areaTop;
            else if (Math.Abs((Top + ActualHeight) - areaBottom) <= snapThreshold) Top = areaBottom - ActualHeight;
        }
        catch
        {
            // Snapping is purely cosmetic; never break dragging.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // The widget hides instead of quitting. The tray icon (or relaunching
        // the executable) brings it back. Use File -> Quit (settings panel) or
        // the tray menu's Quit entry to actually terminate the process.
        Hide();
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BucketViewModel vm }
            && !string.IsNullOrEmpty(vm.AnalyticsUrl))
        {
            OpenUrlSafe(vm.AnalyticsUrl);
        }
    }

    /// <summary>UX-Acc-02 — keyboard activation. Enter / Space opens the
    /// analytics page; Shift+F10 / Apps opens the snooze context menu.</summary>
    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BucketViewModel vm }) return;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                if (!string.IsNullOrEmpty(vm.AnalyticsUrl)) OpenUrlSafe(vm.AnalyticsUrl);
                e.Handled = true;
                break;
            case Key.Apps:
                ShowSnoozeMenu((UIElement)sender, vm);
                e.Handled = true;
                break;
            case Key.F10 when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                ShowSnoozeMenu((UIElement)sender, vm);
                e.Handled = true;
                break;
        }
    }

    private void ShowSnoozeMenu(UIElement target, BucketViewModel vm)
    {
        var bucketId = vm.Id;
        if (string.IsNullOrEmpty(bucketId)) return;

        var menu = new ContextMenu();
        void Add(string header, TimeSpan duration)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => _vm.SnoozeBucket(bucketId, duration);
            menu.Items.Add(item);
        }
        Add("Snooze 1 hour", TimeSpan.FromHours(1));
        Add("Snooze 6 hours", TimeSpan.FromHours(6));
        Add("Snooze 24 hours", TimeSpan.FromHours(24));

        // R4-Q-09 — "until reset" uses the real ResetIso when we have one,
        // otherwise falls back to 8 days (covers max weekly window + slack).
        var untilReset = vm.NextResetLocal.HasValue
            ? vm.NextResetLocal.Value.ToUniversalTime() - DateTimeOffset.UtcNow
            : TimeSpan.FromDays(8);
        if (untilReset < TimeSpan.FromMinutes(5)) untilReset = TimeSpan.FromMinutes(5);
        Add("Snooze until reset", untilReset);

        if (_vm.SettingsStore.Current.Alarms.SnoozedBucketsUntilUtc.ContainsKey(bucketId))
        {
            menu.Items.Add(new Separator());
            var unsnooze = new MenuItem { Header = "Unsnooze" };
            unsnooze.Click += (_, _) => _vm.UnsnoozeBucket(bucketId);
            menu.Items.Add(unsnooze);
        }
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void OnCardRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BucketViewModel vm } el)
        {
            ShowSnoozeMenu(el, vm);
        }
    }

    private void OnToggleSettings(object sender, RoutedEventArgs e) => _vm.Settings.Toggle();

    private void OnResetSettings(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Restore alarm ladder, thresholds, theme and other settings to defaults?\n\n"
            + "Position, autostart, and first-run state are preserved.",
            "QuotaGlass — reset settings",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.OK)
        {
            _vm.Settings.ResetToDefaults();
        }
    }

    private void OnPickWavClicked(object sender, RoutedEventArgs e) => _vm.Settings.PickWavFile(SettingsPanelViewModel.WavSlot.Custom);

    private void OnPickResetWavClicked(object sender, RoutedEventArgs e) => _vm.Settings.PickWavFile(SettingsPanelViewModel.WavSlot.Reset);

    private void OnPickZeroStateWavClicked(object sender, RoutedEventArgs e) => _vm.Settings.PickWavFile(SettingsPanelViewModel.WavSlot.ZeroState);

    // OnDismissSetup / OnRunRegisterClicked / OnOpenUrlFromTag moved into
    // SetupCardView in v0.9 — DataContext was Setup; handlers now live with
    // the UserControl that owns the buttons.

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateChecker.CheckAsync().ConfigureAwait(true);
            if (update is null)
            {
                MessageBox.Show($"QuotaGlass v{UpdateChecker.CurrentVersion} is up to date.",
                    "QuotaGlass", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var msg = $"A new version is available: v{update.LatestVersion}\n\n" +
                      $"Current version: v{UpdateChecker.CurrentVersion}\n" +
                      $"Download: {update.AssetName}\n\n" +
                      "Install now? The app will restart automatically.";
            var result = MessageBox.Show(msg, "QuotaGlass update",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.OK)
            {
                UpdateChecker.LaunchSelfReplace(update);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update check failed: {ex.Message}", "QuotaGlass",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetWidgetPosition()
    {
        Left = 40;
        Top = 40;
        _vm.SettingsStore.Update(s => { s.Widget.X = 40; s.Widget.Y = 40; });
        if (!IsVisible) { Show(); }
        Activate();
        _tray.OnVisibilityChanged(true);
    }

    private static void OpenUrlSafe(string url)
    {
        // F-N6 / setup card buttons. URL must be http(s)://… — we never
        // want a bucket card or setup link weaponized into file://.
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Default browser failure must never crash the widget.
        }
    }
}
