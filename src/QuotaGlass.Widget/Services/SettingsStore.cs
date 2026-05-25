using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// User-editable settings persisted at <see cref="AppPaths.SettingsFile"/>.
/// Atomic write via <see cref="AtomicJsonFile"/>; concurrent reads are safe
/// because the file is only re-read on the UI thread.
/// </summary>
public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private Settings _state;

    public event EventHandler? Changed;

    public Settings Current
    {
        get { lock (_gate) return _state; }
    }

    public SettingsStore() : this(AppPaths.SettingsFile) { }

    public SettingsStore(string path)
    {
        _path = path;
        _state = AtomicJsonFile.Read(path, SettingsJsonContext.Default.Settings)
                 ?? Settings.CreateDefault();
    }

    public void Update(Action<Settings> mutate)
    {
        lock (_gate)
        {
            mutate(_state);
            AtomicJsonFile.Write(_path, _state, SettingsJsonContext.Default.Settings);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Replace(Settings next)
    {
        lock (_gate)
        {
            _state = next;
            AtomicJsonFile.Write(_path, _state, SettingsJsonContext.Default.Settings);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class Settings
{
    /// <summary>How often the bridge expects to push snapshots, in minutes.
    /// Informational on the widget side — actual cadence is set by the
    /// extension. Used here for stale-threshold computation.</summary>
    [JsonPropertyName("refreshMinutes")]
    public int RefreshMinutes { get; set; } = 5;

    [JsonPropertyName("alarms")]
    public AlarmSettings Alarms { get; set; } = new();

    [JsonPropertyName("widget")]
    public WidgetSettings Widget { get; set; } = new();

    [JsonPropertyName("display")]
    public DisplaySettings Display { get; set; } = new();

    public static Settings CreateDefault() => new();
}

public sealed class AlarmSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Lead times in MINUTES (zero = at reset). Default ladder
    /// matches the brief: 24h / 12h / 6h / 3h / 1h / 30m / 15m / 5m /
    /// at-reset.</summary>
    [JsonPropertyName("ladderMinutes")]
    public List<int> LadderMinutes { get; set; } = new() { 24 * 60, 12 * 60, 6 * 60, 3 * 60, 60, 30, 15, 5, 0 };

    [JsonPropertyName("thresholds")]
    public List<double> Thresholds { get; set; } = new() { 75, 90, 95 };

    /// <summary>Absolute path to a WAV played on imminent-reset (R1).</summary>
    [JsonPropertyName("customWavPath")]
    public string? CustomWavPath { get; set; }

    /// <summary>WAV played on renewal-arrived (R2).</summary>
    [JsonPropertyName("resetWavPath")]
    public string? ResetWavPath { get; set; }

    /// <summary>WAV played on zero-state (R3, 100% used).</summary>
    [JsonPropertyName("zeroStateWavPath")]
    public string? ZeroStateWavPath { get; set; }
}

public sealed class WidgetSettings
{
    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("autostart")]
    public bool Autostart { get; set; } = true;

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    /// <summary>
    /// R3-P1-06 — gates the "QuotaGlass is in your tray" balloon tip so it
    /// fires only on the very first run, not on every subsequent launch.
    /// </summary>
    [JsonPropertyName("hasShownFirstRunToast")]
    public bool HasShownFirstRunToast { get; set; }
}

public sealed class DisplaySettings
{
    /// <summary>Ring color thresholds: green &lt; warn &lt; danger.</summary>
    [JsonPropertyName("warnPercent")]
    public double WarnPercent { get; set; } = 60;

    [JsonPropertyName("dangerPercent")]
    public double DangerPercent { get; set; } = 85;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "mocha";
}

[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(AlarmSettings))]
[JsonSerializable(typeof(WidgetSettings))]
[JsonSerializable(typeof(DisplaySettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class SettingsJsonContext : JsonSerializerContext;
