using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;

namespace QuotaGlass.Widget.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Refresh expectation: the extension's default is 5 min (see
    /// AI-Usage_Tracker storage.js defaultSettings.refreshMinutes).
    /// We treat anything older than 2x that as stale, 6x as very stale.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VeryStaleAfter = TimeSpan.FromMinutes(30);

    private readonly SnapshotWatcher _watcher;
    private readonly DispatcherTimer _countdownTimer;
    private readonly AlarmScheduler? _alarms;
    private readonly PaceCalculator _pace = new();
    private DateTimeOffset? _lastSnapshotTs;
    private bool _isStale;
    private string _statusText = "Starting up…";
    private string _statusKind = "info"; // "info" | "stale" | "very-stale" | "error"

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Flat list of buckets across both providers, in deterministic display
    /// order (Claude session → Claude weekly → Codex 5h → Codex weekly →
    /// per-model expansions). Bound to the ItemsControl in MainWindow.
    /// </summary>
    public ObservableCollection<BucketViewModel> Buckets { get; } = new();

    public SetupCardViewModel Setup { get; }

    public SettingsPanelViewModel Settings { get; }

    public SettingsStore SettingsStore { get; }

    /// <summary>NX-07: bound by ring control. True when the OS user has
    /// chosen to minimize animations.</summary>
    public bool ReducedMotion =>
        !System.Windows.SystemParameters.ClientAreaAnimation
        || !System.Windows.SystemParameters.MenuAnimation;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            Raise();
        }
    }

    public string StatusKind
    {
        get => _statusKind;
        private set
        {
            if (_statusKind == value) return;
            _statusKind = value;
            Raise();
        }
    }

    public MainViewModel(Dispatcher dispatcher, AlarmScheduler? alarms = null, SettingsStore? settingsStore = null)
    {
        SettingsStore = settingsStore ?? new SettingsStore();
        Settings = new SettingsPanelViewModel(SettingsStore);
        Setup = new SetupCardViewModel(dispatcher);

        _watcher = new SnapshotWatcher(dispatcher);
        _watcher.SnapshotChanged += OnSnapshot;
        _watcher.SnapshotChanged += (_, m) => _alarms?.OnSnapshot(m);
        _watcher.SnapshotChanged += (_, _) => Setup.Refresh();
        _watcher.StatusChanged += (_, s) => StatusText = s;
        _alarms = alarms;

        if (_alarms is not null)
        {
            ApplyAlarmSettings();
            SettingsStore.Changed += (_, _) => ApplyAlarmSettings();
        }

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _countdownTimer.Tick += (_, _) =>
        {
            foreach (var b in Buckets) b.TickCountdown();
            UpdateStaleness();
        };
    }

    private void ApplyAlarmSettings()
    {
        if (_alarms is null) return;
        var s = SettingsStore.Current.Alarms;
        _alarms.Enabled = s.Enabled;
        _alarms.Ladder = s.LadderMinutes.Select(m => TimeSpan.FromMinutes(m)).ToArray();
        _alarms.UsageThresholds = s.Thresholds.ToArray();
        _alarms.CustomWavPath = s.CustomWavPath;
        _alarms.ResetWavPath = s.ResetWavPath;
        _alarms.ZeroStateWavPath = s.ZeroStateWavPath;
    }

    private void UpdateStaleness()
    {
        if (!_lastSnapshotTs.HasValue) return;
        var age = DateTimeOffset.UtcNow - _lastSnapshotTs.Value;
        var nowStale = age > StaleAfter;
        if (nowStale == _isStale) return;
        _isStale = nowStale;
        var (kind, opacity, prefix) = age > VeryStaleAfter
            ? ("very-stale", 0.5, "STALE — ")
            : age > StaleAfter
                ? ("stale", 0.75, "Stale — ")
                : ("info", 1.0, string.Empty);

        StatusKind = kind;
        StatusText = $"{prefix}Last update: {_lastSnapshotTs.Value.ToLocalTime():t} ({FormatAge(age)} ago)";
        foreach (var b in Buckets) b.SetStale(opacity);
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h";
        if (age.TotalMinutes >= 1) return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalSeconds}s";
    }

    public void Start()
    {
        Setup.Start();
        _watcher.Start();
        _countdownTimer.Start();
        _alarms?.Start();
    }

    private void OnSnapshot(object? sender, SnapshotMessage message)
    {
        _lastSnapshotTs = message.Timestamp;
        _isStale = false;
        StatusKind = "info";
        foreach (var b in Buckets) b.SetStale(1.0);

        var state = message.State;
        if (state is null) return;

        // Walk providers in a stable order so cards don't reshuffle on each
        // refresh. Within a provider, preserve the extension's bucket order.
        var incoming = new List<(string Key, Bucket Bucket)>();
        AppendProvider(incoming, "claude", state.Providers.Claude);
        AppendProvider(incoming, "codex", state.Providers.Codex);

        // Reconcile by Bucket.Id (F-A5). Stable across label / kind changes.
        var existingById = Buckets.ToDictionary(KeyOf, vm => vm);
        var seenIds = new HashSet<string>();

        var desiredOrder = new List<BucketViewModel>(incoming.Count);
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, bucket) in incoming)
        {
            seenIds.Add(bucket.Id);
            if (existingById.TryGetValue(bucket.Id, out var vm))
            {
                vm.Apply(key, bucket);
            }
            else
            {
                vm = new BucketViewModel();
                vm.Apply(key, bucket);
            }
            vm.SetPace(_pace.Forecast(bucket.Id, bucket.PercentUsed, now, bucket.ResetIso));
            desiredOrder.Add(vm);
        }

        // Drop disappeared buckets.
        for (var i = Buckets.Count - 1; i >= 0; i--)
        {
            if (!seenIds.Contains(KeyOf(Buckets[i])))
            {
                Buckets.RemoveAt(i);
            }
        }

        // Reorder to desired order. Cheap because the collection is small.
        for (var i = 0; i < desiredOrder.Count; i++)
        {
            var vm = desiredOrder[i];
            var currentIndex = Buckets.IndexOf(vm);
            if (currentIndex == -1)
            {
                Buckets.Insert(i, vm);
            }
            else if (currentIndex != i)
            {
                Buckets.Move(currentIndex, i);
            }
        }
    }

    private static void AppendProvider(List<(string Key, Bucket Bucket)> dst, string key, ProviderSnapshot? snap)
    {
        if (snap is null) return;
        if (snap.Buckets.Count == 0) return;
        foreach (var bucket in snap.Buckets)
        {
            if (string.IsNullOrWhiteSpace(bucket.Id)) continue;
            dst.Add((key, bucket));
        }
    }

    private static string KeyOf(Bucket b) => b.Id;
    private static string KeyOf(BucketViewModel vm) => vm.Id;

    public void Dispose()
    {
        Setup.Stop();
        _alarms?.Stop();
        _countdownTimer.Stop();
        _watcher.Dispose();
    }

    private void Raise([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
