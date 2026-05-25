using System.Windows;
using System.Windows.Controls;
using QuotaGlass.Widget.ViewModels;

namespace QuotaGlass.Widget.Views;

/// <summary>
/// NX-10 — extracted from MainWindow.xaml in v0.8. DataContext is
/// <see cref="MainViewModel.LogPanel"/>.
/// </summary>
public partial class LogPanelView : UserControl
{
    public LogPanelView() => InitializeComponent();

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogPanelViewModel vm) vm.Toggle();
    }
}
