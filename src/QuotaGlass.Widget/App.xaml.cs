using System.Runtime.InteropServices;
using System.Windows;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;
using Application = System.Windows.Application;

namespace QuotaGlass.Widget;

public partial class App : Application
{
    // R3-P1-01 — single-instance lock. A second launch focuses the first
    // window and exits, instead of racing two FileSystemWatchers and two
    // SettingsStore writers against the same %LOCALAPPDATA% files.
    private const string InstanceMutexName = "Global\\QuotaGlass.Widget.Instance.4F1B3F6E";
    private static Mutex? _instanceMutex;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    private const int SwRestore = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();
        WidgetLogger.Init();
        WidgetLogger.Info($"QuotaGlass.Widget starting; pid={Environment.ProcessId}; argc={e.Args.Length}");

        // Acquire the named mutex BEFORE any window comes up so duplicate
        // launches collapse silently to the existing instance.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var ownedByUs);
        if (!ownedByUs)
        {
            WidgetLogger.Info("Another QuotaGlass.Widget instance is already running; focusing existing and exiting.");
            try
            {
                var existing = FindWindowW(null, "QuotaGlass");
                if (existing != IntPtr.Zero)
                {
                    ShowWindow(existing, SwRestore);
                    SetForegroundWindow(existing);
                }
            }
            catch { /* best-effort */ }
            Shutdown();
            return;
        }

        // Capture otherwise-silent dispatcher faults to the log file.
        DispatcherUnhandledException += (_, ex) =>
        {
            WidgetLogger.Error("Unhandled dispatcher exception", ex.Exception);
            // Let the default handler decide whether to crash; we just observe.
        };

        // Dev-mode hook: write a deterministic snapshot then continue normal
        // startup. Lets developers iterate on the widget without spinning up
        // the full extension → NMH chain.
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, "--inject-fake-snapshot", StringComparison.OrdinalIgnoreCase))
            {
                FakeSnapshotInjector.Inject();
                WidgetLogger.Info("Injected fake snapshot for dev mode");
            }
        }

        // NX-06 — apply the persisted theme before any window comes up so
        // we don't flash a dark-themed window into a light-themed one.
        try
        {
            var settings = new SettingsStore();
            ThemeService.Apply(settings.Current.Display.Theme);
        }
        catch
        {
            // Theme apply must never block startup.
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // ReleaseMutex throws if not owned; harmless on shutdown.
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
