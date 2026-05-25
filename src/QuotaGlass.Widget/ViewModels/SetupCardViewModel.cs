using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Threading;
using QuotaGlass.Widget.Services;

namespace QuotaGlass.Widget.ViewModels;

/// <summary>
/// First-run Setup Checklist (F-N3). Shown when any precondition is unmet.
/// Probes once per second; collapses to nothing when all checks pass.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SetupCardViewModel : INotifyPropertyChanged
{
    private readonly Func<HealthSnapshot> _probe;
    private readonly DispatcherTimer _timer;
    private readonly SettingsStore? _settings;
    private HealthSnapshot _last;
    private bool _isVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            Raise();
        }
    }

    public string ExtensionStepLabel => _last.ExtensionLikelyInstalled
        ? "✓ AI-Usage_Tracker extension installed"
        : "○ Install the AI-Usage_Tracker extension";

    public string NmhStepLabel => _last.NmhRegistered
        ? "✓ Native messaging registered"
        : "○ Register native messaging (run QuotaGlass.NMH.exe --register)";

    public string SnapshotStepLabel => _last.FirstSnapshotReceived
        ? "✓ First snapshot received"
        : "○ Waiting for first snapshot…";

    public string ExtensionInstallUrl => "https://github.com/SysAdminDoc/AI-Usage_Tracker/releases/latest";

    public string TroubleshootingUrl => "https://github.com/SysAdminDoc/QuotaGlass#install";

    public SetupCardViewModel(Dispatcher dispatcher, SettingsStore? settings = null, Func<HealthSnapshot>? probe = null)
    {
        _settings = settings;
        _probe = probe ?? new HealthCheck().Probe;
        _last = _probe();
        _isVisible = ShouldBeVisible(_last);

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>R3-P1-07 — dismiss the Setup card for 24 hours.</summary>
    public void DismissForDay()
    {
        if (_settings is null) return;
        _settings.Update(s => s.Widget.SetupCardDismissedUntilUtc = DateTimeOffset.UtcNow.AddDays(1));
        IsVisible = false;
    }

    private bool ShouldBeVisible(HealthSnapshot snapshot)
    {
        if (snapshot.AllGood) return false;
        var until = _settings?.Current.Widget.SetupCardDismissedUntilUtc;
        if (until.HasValue && until.Value > DateTimeOffset.UtcNow) return false;
        return true;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    public void Refresh()
    {
        var next = _probe();
        var visible = ShouldBeVisible(next);
        if (next == _last)
        {
            IsVisible = visible;
            return;
        }
        _last = next;
        IsVisible = visible;
        Raise(nameof(ExtensionStepLabel));
        Raise(nameof(NmhStepLabel));
        Raise(nameof(SnapshotStepLabel));
    }

    private void Raise([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
