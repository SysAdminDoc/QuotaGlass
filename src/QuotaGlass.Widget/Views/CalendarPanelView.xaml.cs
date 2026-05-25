using System.Windows;
using System.Windows.Controls;
using QuotaGlass.Widget.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace QuotaGlass.Widget.Views;

/// <summary>
/// L-02 — extracted from MainWindow.xaml in v0.8. DataContext is the
/// owning MainViewModel's <see cref="MainViewModel.Calendar"/>; the toggle
/// button delegates to <see cref="CalendarViewModel.Toggle"/>.
/// </summary>
public partial class CalendarPanelView : UserControl
{
    public CalendarPanelView() => InitializeComponent();

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalendarViewModel vm) vm.Toggle();
    }
}
