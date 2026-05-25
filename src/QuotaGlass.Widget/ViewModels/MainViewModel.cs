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
    private string _statusText = "Starting up…";

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public MainViewModel(Dispatcher dispatcher)
    {
        _watcher = new SnapshotWatcher(dispatcher);
        _watcher.SnapshotChanged += OnSnapshot;
        _watcher.StatusChanged += (_, s) => StatusText = s;

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
    }

    private void OnSnapshot(object? sender, BucketSnapshot snap)
    {
        var existingByKey = Buckets.ToDictionary(KeyOf, v => v);
        var seenKeys = new HashSet<string>();

        foreach (var bucket in snap.Buckets)
        {
            var key = KeyOf(bucket);
            seenKeys.Add(key);

            if (existingByKey.TryGetValue(key, out var vm))
            {
                vm.Apply(bucket);
            }
            else
            {
                var fresh = new BucketViewModel();
                fresh.Apply(bucket);
                Buckets.Add(fresh);
            }
        }

        for (var i = Buckets.Count - 1; i >= 0; i--)
        {
            if (!seenKeys.Contains(KeyOf(Buckets[i])))
            {
                Buckets.RemoveAt(i);
            }
        }
    }

    private static string KeyOf(Bucket b) => $"{b.Provider}/{b.Label}";

    private static string KeyOf(BucketViewModel vm) => $"{vm.Provider}/{vm.Label}";

    public void Dispose()
    {
        _countdownTimer.Stop();
        _watcher.Dispose();
    }

    private void Raise([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
