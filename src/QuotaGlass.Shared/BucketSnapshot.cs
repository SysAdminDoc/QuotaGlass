using System.Text.Json.Serialization;

namespace QuotaGlass.Shared;

public enum Provider
{
    Unknown = 0,
    Claude = 1,
    Codex = 2,
}

public enum SnapshotSource
{
    Unknown = 0,
    Api = 1,
    Stream = 2,
    Headers = 3,
    Dom = 4,
    SilentTab = 5,
}

public sealed class Bucket
{
    [JsonPropertyName("provider")]
    public Provider Provider { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    [JsonPropertyName("resetIso")]
    public DateTimeOffset? ResetIso { get; set; }

    [JsonPropertyName("source")]
    public SnapshotSource Source { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public sealed class BucketSnapshot
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "snapshot";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("extensionVersion")]
    public string? ExtensionVersion { get; set; }

    [JsonPropertyName("buckets")]
    public List<Bucket> Buckets { get; set; } = new();
}

[JsonSerializable(typeof(BucketSnapshot))]
[JsonSerializable(typeof(Bucket))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
public partial class SnapshotJsonContext : JsonSerializerContext;
