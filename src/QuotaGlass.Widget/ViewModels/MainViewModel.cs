using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;

namespace QuotaGlass.Widget.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SnapshotWatcher _watcher;
    private readonly DispatcherTimer _countdownTimer;
    private readonly AlarmScheduler? _alarms;
    private string _statusText = "Starting up…";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Flat list of buckets across both providers, in deterministic display
    /// order (Claude session → Claude weekly → Codex 5h → Codex weekly →
    /// per-model expansions). Bound to the ItemsControl in MainWindow.
    /// </summary>
    public ObservableCollection<BucketViewModel> Buckets { get; } = new();

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

    public MainViewModel(Dispatcher dispatcher, AlarmScheduler? alarms = null)
    {
        _watcher = new SnapshotWatcher(dispatcher);
        _watcher.SnapshotChanged += OnSnapshot;
        _watcher.SnapshotChanged += (_, m) => _alarms?.OnSnapshot(m);
        _watcher.StatusChanged += (_, s) => StatusText = s;
        _alarms = alarms;

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _countdownTimer.Tick += (_, _) =>
        {
            foreach (var b in Buckets) b.TickCountdown();
        };
    }

    public void Start()
    {
        _watcher.Start();
        _countdownTimer.Start();
        _alarms?.Start();
    }

    private void OnSnapshot(object? sender, SnapshotMessage message)
    {
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
        _alarms?.Stop();
        _countdownTimer.Stop();
        _watcher.Dispose();
    }

    private void Raise([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
