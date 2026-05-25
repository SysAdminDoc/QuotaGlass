using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QuotaGlass.Widget.Services;
using QuotaGlass.Widget.ViewModels;

namespace QuotaGlass.Widget.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
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
        Loaded += (_, _) => _vm.Start();
        Closed += (_, _) =>
        {
            _topMost?.Dispose();
            _vm.Dispose();
        };
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
}
