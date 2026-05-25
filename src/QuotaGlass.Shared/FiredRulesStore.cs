using System.IO;
using System.Text.Json.Serialization;

namespace QuotaGlass.Shared;

/// <summary>
/// Persists fire-once idempotency keys so the same notification never
/// re-fires inside a reset window — even across widget restarts. Stored at
/// <c>%LOCALAPPDATA%\QuotaGlass\fired-rules.json</c> so a purge clears
/// everything in one shot.
///
/// Keys older than <see cref="RetainDays"/> are pruned on load.
///
/// v0.6 — moved from Widget/Services into Shared so the unit tests can
/// exercise the prune + idempotency behavior without pulling WPF in.
/// </summary>
public sealed class FiredRulesStore
{
    public const int RetainDays = 14;

    private readonly object _gate = new();
    private readonly string _path;
    private FiredRulesState _state;

    public FiredRulesStore() : this(Path.Combine(AppPaths.LocalAppDataRoot, "fired-rules.json")) { }

    public FiredRulesStore(string path)
    {
        _path = path;
        _state = AtomicJsonFile.Read(path, FiredRulesJsonContext.Default.FiredRulesState)
                 ?? new FiredRulesState();
        Prune();
    }

    public bool HasFired(string key)
    {
        lock (_gate)
        {
            return _state.Fired.ContainsKey(key);
        }
    }

    public void MarkFired(string key)
    {
        lock (_gate)
        {
            _state.Fired[key] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Save();
        }
    }

    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetainDays).ToUnixTimeMilliseconds();
        var toRemove = _state.Fired.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        if (toRemove.Count == 0) return;
        foreach (var key in toRemove) _state.Fired.Remove(key);
        Save();
    }

    private void Save()
    {
        try
        {
            AtomicJsonFile.Write(_path, _state, FiredRulesJsonContext.Default.FiredRulesState);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class FiredRulesState
{
    [JsonPropertyName("fired")]
    public Dictionary<string, long> Fired { get; set; } = new();
}

[JsonSerializable(typeof(FiredRulesState))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class FiredRulesJsonContext : JsonSerializerContext;
