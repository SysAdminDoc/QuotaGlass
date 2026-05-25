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

        // Dev-mode hook: write a deterministic snapshot then continue normal
        // startup. Lets developers iterate on the widget without spinning up
        // the full extension → NMH chain.
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, "--inject-fake-snapshot", StringComparison.OrdinalIgnoreCase))
            {
                FakeSnapshotInjector.Inject();
            }
        }

        base.OnStartup(e);
    }
}
