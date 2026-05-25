using System.Windows;
using System.Windows.Input;
using QuotaGlass.Widget.ViewModels;

namespace QuotaGlass.Widget.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(Dispatcher);
        DataContext = _vm;
        Loaded += (_, _) => _vm.Start();
        Closed += (_, _) => _vm.Dispose();
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
        Close();
    }
}
