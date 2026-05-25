using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// R3-P2-02 — durable per-bucket history ring buffer at
/// <c>%LOCALAPPDATA%\QuotaGlass\history.json</c>. Stores up to
/// <see cref="MaxSamplesPerBucket"/> recent (ts, percentUsed) samples per
/// bucket Id so the v0.2.0 sparkline panel has data to render even right
/// after a widget restart.
///
/// Why not bundle history in the wire schema? See RESEARCH_PASS_3 Bug 4:
/// the extension snapshot does NOT carry history today; widget-side
/// buffering avoids a cross-repo schema bump.
/// </summary>
public sealed class HistoryStore
{
    public const int MaxSamplesPerBucket = 24;

    private readonly object _gate = new();
    private readonly string _path;
    private HistoryState _state;
    private bool _pendingWrite;

    public HistoryStore() : this(Path.Combine(AppPaths.LocalAppDataRoot, "history.json")) { }

    public HistoryStore(string path)
    {
        _path = path;
        _state = AtomicJsonFile.Read(path, HistoryJsonContext.Default.HistoryState)
                 ?? new HistoryState();
    }

    /// <summary>
    /// Append a sample for one bucket. Caller passes the snapshot timestamp
    /// (the wire-level <c>ts</c> field) so duplicate writes from a single
    /// snapshot are no-ops. R4-P1-01 — does NOT write to disk; call
    /// <see cref="Flush"/> once after a snapshot batch is processed.
    /// </summary>
    public void AppendSample(string bucketId, DateTimeOffset ts, double percentUsed)
    {
        if (string.IsNullOrEmpty(bucketId)) return;

        lock (_gate)
        {
            if (!_state.Buckets.TryGetValue(bucketId, out var samples) || samples is null)
            {
                samples = new List<HistorySample>();
                _state.Buckets[bucketId] = samples;
            }
            // Dedupe by ts so a snapshot-debounce burst doesn't replay.
            if (samples.Count > 0 && samples[^1].Ts == ts) return;

            samples.Add(new HistorySample { Ts = ts, PercentUsed = percentUsed });
            while (samples.Count > MaxSamplesPerBucket) samples.RemoveAt(0);
            _pendingWrite = true;
        }
    }

    /// <summary>
    /// R4-P1-01 — persist all pending appends in a single atomic write.
    /// Caller (MainViewModel.OnSnapshot) invokes this once per snapshot
    /// batch instead of fsync'ing per bucket.
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_pendingWrite) return;
            _pendingWrite = false;
            Save();
        }
    }

    public IReadOnlyList<HistorySample> Read(string bucketId)
    {
        lock (_gate)
        {
            return _state.Buckets.TryGetValue(bucketId, out var samples) && samples is not null
                ? samples.ToArray()
                : Array.Empty<HistorySample>();
        }
    }

    private void Save()
    {
        try
        {
            AtomicJsonFile.Write(_path, _state, HistoryJsonContext.Default.HistoryState);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class HistoryState
{
    [JsonPropertyName("buckets")]
    public Dictionary<string, List<HistorySample>> Buckets { get; set; } = new();
}

[JsonSerializable(typeof(HistoryState))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class HistoryJsonContext : JsonSerializerContext;
