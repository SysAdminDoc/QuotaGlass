using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using QuotaGlass.Widget.Services;
using QuotaGlass.Widget.ViewModels;
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
        _tray.SettingsRequested += (_, _) => { Show(); Activate(); /* TODO N-15 expand panel */ };
        _tray.QuitRequested += (_, _) => System.Windows.Application.Current.Shutdown();

        _vm.Buckets.CollectionChanged += (_, _) => RefreshTrayBadge();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Buckets)) RefreshTrayBadge();
        };

        Loaded += (_, _) =>
        {
            _vm.Start();
            _tray.NotifyFirstRun();
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
    }

    private void OnChromeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
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

    private void OnOpenUrlFromTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
        {
            OpenUrlSafe(url);
        }
    }

    private void OnRunRegisterClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var nmhPath = Path.Combine(exeDir, "QuotaGlass.NMH.exe");
            if (!File.Exists(nmhPath))
            {
                MessageBox.Show("QuotaGlass.NMH.exe not found alongside the widget. Run the installer.",
                    "QuotaGlass", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = nmhPath,
                Arguments = "--register",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Register failed: {ex.Message}", "QuotaGlass",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
