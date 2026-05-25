using System.Windows;
using QuotaGlass.Widget.ViewModels;
using Application = System.Windows.Application;

namespace QuotaGlass.Widget.Services;

internal sealed class TrayCoordinator : IDisposable
{
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly Func<Task> _checkForUpdates;
    private readonly Action _resetPosition;
    private readonly TrayIconService _tray = new();

    public TrayCoordinator(
        Window window,
        MainViewModel vm,
        Func<Task> checkForUpdates,
        Action resetPosition)
    {
        _window = window;
        _vm = vm;
        _checkForUpdates = checkForUpdates;
        _resetPosition = resetPosition;

        _tray.ShowRequested += (_, _) => ShowWindow();
        _tray.HideRequested += (_, _) => HideWindow();
        _tray.RefreshRequested += (_, _) => { };
        _tray.SettingsRequested += (_, _) =>
        {
            ShowWindow();
            _vm.Settings.IsExpanded = true;
        };
        _tray.CheckForUpdatesRequested += async (_, _) => await _checkForUpdates().ConfigureAwait(true);
        _tray.ResetPositionRequested += (_, _) => _resetPosition();
        _tray.QuitRequested += (_, _) => Application.Current.Shutdown();

        _vm.Buckets.CollectionChanged += (_, _) => RefreshBadge();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Buckets)) RefreshBadge();
        };
    }

    public void NotifyFirstRun() => _tray.NotifyFirstRun();

    public void OnVisibilityChanged(bool isVisible) => _tray.OnVisibilityChanged(isVisible);

    public void RefreshBadge()
    {
        double worst = 0;
        foreach (var bucket in _vm.Buckets)
        {
            if (bucket.Percent > worst) worst = bucket.Percent;
        }
        _tray.UpdateBadge(worst);
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.Activate();
        _tray.OnVisibilityChanged(true);
    }

    private void HideWindow()
    {
        _window.Hide();
        _tray.OnVisibilityChanged(false);
    }

    public void Dispose() => _tray.Dispose();
}
