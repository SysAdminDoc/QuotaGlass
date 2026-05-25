using System.Text.Json.Serialization;

namespace QuotaGlass.Shared;

/// <summary>
/// One per-bucket history sample as captured by the widget's
/// <c>HistoryStore</c>. Pure data — lives in Shared so anomaly detection
/// and other consumers can be unit-tested without pulling WPF in.
/// </summary>
public sealed class HistorySample
{
    [JsonPropertyName("ts")]
    public DateTimeOffset Ts { get; set; }

    [JsonPropertyName("percentUsed")]
    public double PercentUsed { get; set; }
}
