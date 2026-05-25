using System.Windows;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;
using Application = System.Windows.Application;

namespace QuotaGlass.Widget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();
        WidgetLogger.Init();
        WidgetLogger.Info($"QuotaGlass.Widget starting; pid={Environment.ProcessId}; argc={e.Args.Length}");

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

        base.OnStartup(e);
    }
}
