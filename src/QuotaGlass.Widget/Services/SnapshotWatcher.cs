using System.IO;
using System.Windows;
using System.Windows.Threading;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Watches the on-disk snapshot file written by QuotaGlass.NMH and raises
/// <see cref="SnapshotChanged"/> with the latest decoded message. Debounced
/// to coalesce rapid burst writes from atomic-replace patterns.
/// </summary>
public sealed class SnapshotWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce;
    private readonly Dispatcher _dispatcher;

    public event EventHandler<SnapshotMessage>? SnapshotChanged;
    public event EventHandler<string>? StatusChanged;

    public SnapshotMessage? Latest { get; private set; }

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
        _dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void ReloadAndPublish()
    {
        var message = AtomicJsonFile.Read(AppPaths.SnapshotFile, SnapshotJsonContext.Default.SnapshotMessage);
        if (message is null)
        {
            StatusChanged?.Invoke(this, "Waiting for first snapshot from extension…");
            return;
        }

        // Tolerate older schema versions (none yet) by ignoring; future
        // migration code lives in SchemaMigrator.
        if (!SchemaVersion.IsSupported(message.SchemaVersion))
        {
            StatusChanged?.Invoke(this, $"Snapshot schema v{message.SchemaVersion} not supported (supported: {SchemaVersion.Min}..{SchemaVersion.Max}).");
            return;
        }

        Latest = message;
        SnapshotChanged?.Invoke(this, message);
        StatusChanged?.Invoke(this, $"Last update: {message.Timestamp.ToLocalTime():t}");
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
