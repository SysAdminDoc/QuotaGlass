using System.IO;
using System.Windows;
using System.Windows.Threading;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Watches the on-disk snapshot file written by QuotaGlass.NMH and raises
/// <see cref="SnapshotChanged"/> with the latest decoded snapshot. Debounced
/// to coalesce rapid burst writes from atomic-replace patterns.
/// </summary>
public sealed class SnapshotWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce;
    private readonly Dispatcher _dispatcher;

    public event EventHandler<BucketSnapshot>? SnapshotChanged;
    public event EventHandler<string>? StatusChanged;

    public BucketSnapshot? Latest { get; private set; }

    public SnapshotWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        AppPaths.EnsureCreated();

        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ReloadAndPublish();
        };

        _watcher = new FileSystemWatcher(AppPaths.LocalAppDataRoot)
        {
            Filter = Path.GetFileName(AppPaths.SnapshotFile),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = false,
        };

        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        ReloadAndPublish(); // prime on launch
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Marshal debounce kick to the UI thread.
        _dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void ReloadAndPublish()
    {
        var snap = AtomicJsonFile.Read(AppPaths.SnapshotFile, SnapshotJsonContext.Default.BucketSnapshot);
        if (snap is null)
        {
            StatusChanged?.Invoke(this, "Waiting for first snapshot from extension…");
            return;
        }

        Latest = snap;
        SnapshotChanged?.Invoke(this, snap);
        StatusChanged?.Invoke(this, $"Last update: {snap.Timestamp.ToLocalTime():t}");
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
