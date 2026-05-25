using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuotaGlass.Widget.ViewModels;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace QuotaGlass.Widget.Views;

/// <summary>
/// F-N3 — extracted from MainWindow.xaml in v0.9. DataContext is
/// <see cref="MainViewModel.Setup"/>. The Install / Help buttons reuse the
/// URL-safe `Process.Start` helper; Run-register spawns the NMH binary
/// next to the widget EXE; Dismiss-24h delegates to the viewmodel.
/// </summary>
public partial class SetupCardView : UserControl
{
    public SetupCardView() => InitializeComponent();

    private void OnOpenUrl(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
        {
            OpenUrlSafe(url);
        }
    }

    private void OnDismiss(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupCardViewModel vm) vm.DismissForDay();
    }

    private void OnRunRegister(object sender, RoutedEventArgs e)
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
        // Mirrors MainWindow's URL-scheme guard so a tagged Setup button
        // can't be weaponized into a file:// or shell: target.
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
            // Default-browser failure must never crash the widget.
        }
    }
}
