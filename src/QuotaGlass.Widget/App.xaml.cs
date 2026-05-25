using System.Windows;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();
        base.OnStartup(e);
    }
}
