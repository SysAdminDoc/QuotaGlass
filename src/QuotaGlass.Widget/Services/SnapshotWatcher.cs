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

        // R4-P1-02 — watch both the canonical snapshot.json (extension chain)
        // and the sibling snapshot.local-creds.json (`--poll-credentials`).
        // The wildcard filter `snapshot*.json` covers both without a second
        // FileSystemWatcher instance.
        _watcher = new FileSystemWatcher(AppPaths.LocalAppDataRoot)
        {
            Filter = "snapshot*.json",
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

    public void Refresh() => ReloadAndPublish();

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
        var extension = AtomicJsonFile.Read(AppPaths.SnapshotFile, SnapshotJsonContext.Default.SnapshotMessage);
        var localCreds = AtomicJsonFile.Read(AppPaths.LocalCredsSnapshotFile, SnapshotJsonContext.Default.SnapshotMessage);

        var message = Merge(extension, localCreds);
        if (message is null)
        {
            StatusChanged?.Invoke(this, "Waiting for first snapshot from extension…");
            return;
        }

        if (!SchemaVersion.IsSupported(message.SchemaVersion))
        {
            StatusChanged?.Invoke(this, $"Snapshot schema v{message.SchemaVersion} not supported (supported: {SchemaVersion.Min}..{SchemaVersion.Max}).");
            return;
        }

        Latest = message;
        SnapshotChanged?.Invoke(this, message);
        StatusChanged?.Invoke(this, $"Last update: {message.Timestamp.ToLocalTime():t}");
    }

    /// <summary>
    /// R4-P1-02 — merge two snapshot sources (extension chain + local
    /// credential poller). Rules: when both are present, the newer one
    /// wins for the envelope timestamp; per-provider snapshots use the
    /// extension's data when available (richer per-model buckets) and
    /// fall back to the credential poller's data only when the extension
    /// is missing or reports <c>Ok=false</c>.
    /// </summary>
    internal static SnapshotMessage? Merge(SnapshotMessage? ext, SnapshotMessage? creds)
    {
        if (ext is null) return creds;
        if (creds is null) return ext;

        SnapshotMessage primary, secondary;
        if (ext.Timestamp >= creds.Timestamp)
        {
            primary = ext; secondary = creds;
        }
        else
        {
            primary = creds; secondary = ext;
        }

        var merged = new SnapshotMessage
        {
            Kind = primary.Kind,
            SchemaVersion = primary.SchemaVersion,
            Timestamp = primary.Timestamp,
            ExtensionVersion = primary.ExtensionVersion,
            State = new ExtensionState
            {
                FetchedAtIso = primary.State?.FetchedAtIso,
                Providers = new ProviderMap
                {
                    Claude = PickProvider(primary.State?.Providers.Claude, secondary.State?.Providers.Claude),
                    Codex = PickProvider(primary.State?.Providers.Codex, secondary.State?.Providers.Codex),
                    ClaudeAccounts = PickProviderList(primary.State?.Providers.ClaudeAccounts, secondary.State?.Providers.ClaudeAccounts),
                    CodexAccounts = PickProviderList(primary.State?.Providers.CodexAccounts, secondary.State?.Providers.CodexAccounts),
                },
                History = MergeHistory(primary.State?.History, secondary.State?.History),
            },
        };
        return merged;
    }

    private static ProviderSnapshot? PickProvider(ProviderSnapshot? primary, ProviderSnapshot? secondary)
    {
        if (primary is null) return secondary;
        if (secondary is null) return primary;
        // Prefer the producer that succeeded; on a tie, the primary (newest)
        // wins so the user sees the freshest data.
        if (!primary.Ok && secondary.Ok) return secondary;
        return primary;
    }

    private static List<ProviderSnapshot>? PickProviderList(List<ProviderSnapshot>? primary, List<ProviderSnapshot>? secondary)
    {
        if (primary is { Count: > 0 }) return primary;
        return secondary is { Count: > 0 } ? secondary : null;
    }

    private static Dictionary<string, List<HistorySample>>? MergeHistory(
        Dictionary<string, List<HistorySample>>? primary,
        Dictionary<string, List<HistorySample>>? secondary)
    {
        if (primary is null || primary.Count == 0) return secondary;
        if (secondary is null || secondary.Count == 0) return primary;

        var merged = new Dictionary<string, List<HistorySample>>(secondary, StringComparer.Ordinal);
        foreach (var (bucketId, samples) in primary)
        {
            merged[bucketId] = samples;
        }
        return merged;
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
