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
    private readonly HistoryStore _history = new();
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

    public HistoryStore History => _history;

    public SetupCardViewModel Setup { get; }

    public SettingsPanelViewModel Settings { get; }

    public LogPanelViewModel LogPanel { get; }

    public CalendarViewModel Calendar { get; } = new();

    public SettingsStore SettingsStore { get; }

    /// <summary>NX-07 / R4-Q-05 — bound by ring control. True when the OS
    /// user has chosen to minimize animations. Re-evaluated whenever
    /// <see cref="System.Windows.SystemParameters"/> raises a static change
    /// notification so runtime accessibility-preference flips propagate.</summary>
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
        Setup = new SetupCardViewModel(dispatcher, SettingsStore);
        LogPanel = new LogPanelViewModel(dispatcher);

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

        // R4-Q-05 — runtime accessibility-preference flips propagate.
        System.Windows.SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(System.Windows.SystemParameters.ClientAreaAnimation)
                or nameof(System.Windows.SystemParameters.MenuAnimation))
            {
                Raise(nameof(ReducedMotion));
            }
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
        _alarms.WebhookCommand = s.WebhookCommand;
        _alarms.RespectFocusAssist = s.RespectFocusAssist;
        _alarms.PaceEnabled = s.PaceEnabled;
        _alarms.SnoozedUntil = new Dictionary<string, DateTimeOffset>(s.SnoozedBucketsUntilUtc);
    }

    public void SnoozeBucket(string bucketId, TimeSpan duration)
    {
        if (string.IsNullOrEmpty(bucketId)) return;
        var until = DateTimeOffset.UtcNow + duration;
        SettingsStore.Update(s => s.Alarms.SnoozedBucketsUntilUtc[bucketId] = until);
    }

    public void UnsnoozeBucket(string bucketId)
    {
        if (string.IsNullOrEmpty(bucketId)) return;
        SettingsStore.Update(s => s.Alarms.SnoozedBucketsUntilUtc.Remove(bucketId));
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

        // L-07 — fill in best-effort plan label when the extension didn't.
        if (state.Providers.Claude is { } cp) cp.Plan = PlanInference.Infer(cp);
        if (state.Providers.Codex is { } xp) xp.Plan = PlanInference.Infer(xp);

        // Walk providers in a stable order so cards don't reshuffle on each
        // refresh. Within a provider, preserve the extension's bucket order.
        var incoming = new List<(string Key, Bucket Bucket, string? Account)>();
        AppendProvider(incoming, "claude", state.Providers.Claude);
        AppendProvider(incoming, "codex", state.Providers.Codex);

        // Reconcile by Bucket.Id (F-A5). Stable across label / kind changes.
        var existingById = Buckets.ToDictionary(KeyOf, vm => vm);
        var seenIds = new HashSet<string>();

        var desiredOrder = new List<BucketViewModel>(incoming.Count);
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, bucket, account) in incoming)
        {
            seenIds.Add(bucket.Id);
            if (existingById.TryGetValue(bucket.Id, out var vm))
            {
                vm.Apply(key, bucket, account);
            }
            else
            {
                vm = new BucketViewModel();
                vm.Apply(key, bucket, account);
            }
            vm.SetPace(_pace.Forecast(bucket.Id, bucket.PercentUsed, now, bucket.ResetIso));
            // R3-P2-02 — feed the durable history buffer that powers NX-08
            // sparklines. Dedupe by snapshot ts inside HistoryStore.
            _history.AppendSample(bucket.Id, message.Timestamp, bucket.PercentUsed);
            var hist = _history.Read(bucket.Id);
            vm.SetSparklineData(hist);
            // L-09 — keep AlarmScheduler's per-bucket history in lockstep.
            _alarms?.UpdateHistory(bucket.Id, hist);
            desiredOrder.Add(vm);
        }

        // R4-P1-01 — one fsync per snapshot batch instead of one per bucket.
        _history.Flush();

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

        // L-02 — refresh the 7-day reset calendar from the latest snapshot.
        Calendar.Rebuild(Buckets);
    }

    private static void AppendProvider(List<(string Key, Bucket Bucket, string? Account)> dst, string key, ProviderSnapshot? snap)
    {
        if (snap is null) return;
        if (snap.Buckets.Count == 0) return;
        var account = ShortAccount(snap.OrgId ?? snap.AccountId);
        foreach (var bucket in snap.Buckets)
        {
            if (string.IsNullOrWhiteSpace(bucket.Id)) continue;
            dst.Add((key, bucket, account));
        }
    }

    private static string? ShortAccount(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        // Show last 8 chars; orgIds are UUIDs in practice, accountIds are long.
        return id.Length <= 8 ? id : "…" + id[^8..];
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
