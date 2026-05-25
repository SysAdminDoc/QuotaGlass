using System.Text.Json.Serialization;

namespace QuotaGlass.Shared;

// Canonical wire shape — see docs/extension-integration.md
//
// The extension's state envelope from AI-Usage_Tracker/src/lib/storage.js
// defaultState(). We mirror it 1:1 so deserialization is lossless and the
// notification rule keys can be reconstructed exactly.

/// <summary>
/// Top-level message envelope. Always sent over native messaging.
/// </summary>
public sealed class SnapshotMessage
{
    /// <summary>Either "snapshot" or "ping". Required.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "snapshot";

    /// <summary>Wire schema version. Current = 1. Required for snapshot kind.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>UTC timestamp the extension produced this frame.</summary>
    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Informational only; not gated on.</summary>
    [JsonPropertyName("extensionVersion")]
    public string? ExtensionVersion { get; set; }

    /// <summary>State envelope. Null for ping frames.</summary>
    [JsonPropertyName("state")]
    public ExtensionState? State { get; set; }
}

/// <summary>
/// Mirrors AI-Usage_Tracker `state.snapshot`. Per-provider map keyed by
/// well-known provider names ("claude", "codex"). Either may be null/missing
/// if that provider has not produced data yet.
/// </summary>
public sealed class ExtensionState
{
    [JsonPropertyName("fetchedAtISO")]
    public DateTimeOffset? FetchedAtIso { get; set; }

    [JsonPropertyName("providers")]
    public ProviderMap Providers { get; set; } = new();

    /// <summary>
    /// Schema v2 (R4-N3) — optional per-bucket history (up to ~24 samples each).
    /// Lets the widget render sparklines on a fresh install without waiting
    /// hours to accumulate samples locally. Receivers running schema v1 ignore
    /// this field; widget merges it into HistoryStore when present.
    /// </summary>
    [JsonPropertyName("history")]
    public Dictionary<string, List<HistorySample>>? History { get; set; }
}

public sealed class ProviderMap
{
    [JsonPropertyName("claude")]
    public ProviderSnapshot? Claude { get; set; }

    [JsonPropertyName("codex")]
    public ProviderSnapshot? Codex { get; set; }

    /// <summary>
    /// Schema v3 (R4-N5 / R3-P2-01) — multi-account support. When a user has
    /// two or more Claude accounts (e.g. personal + work), the extension
    /// emits one <see cref="ProviderSnapshot"/> per account here. Older
    /// receivers (schema v1/v2) ignore the field and just see the primary
    /// account via <see cref="Claude"/>.
    /// </summary>
    [JsonPropertyName("claudeAccounts")]
    public List<ProviderSnapshot>? ClaudeAccounts { get; set; }

    /// <summary>Schema v3 — same as <see cref="ClaudeAccounts"/> but for Codex.</summary>
    [JsonPropertyName("codexAccounts")]
    public List<ProviderSnapshot>? CodexAccounts { get; set; }
}

/// <summary>
/// One provider's most recent reading. `Ok=false` cases still get stored so
/// the widget can surface the error with retain-last-good semantics.
/// </summary>
public sealed class ProviderSnapshot
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("orgId")]
    public string? OrgId { get; set; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("buckets")]
    public List<Bucket> Buckets { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("fallbackError")]
    public string? FallbackError { get; set; }
}

/// <summary>
/// One quota window. The widget reconciles its ObservableCollection by
/// <see cref="Id"/>, never by <see cref="Label"/> — labels are presentation-
/// only and drift between extension versions.
/// </summary>
public sealed class Bucket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;  // "session" | "5h" | "weekly"

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("percentUsed")]
    public double PercentUsed { get; set; }

    [JsonPropertyName("resetISO")]
    public DateTimeOffset? ResetIso { get; set; }

    [JsonPropertyName("rawResetText")]
    public string? RawResetText { get; set; }
}

[JsonSerializable(typeof(SnapshotMessage))]
[JsonSerializable(typeof(ExtensionState))]
[JsonSerializable(typeof(ProviderMap))]
[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(Bucket))]
[JsonSerializable(typeof(HistorySample))]
[JsonSerializable(typeof(Dictionary<string, List<HistorySample>>))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    // R2-P1-02: cap inbound JSON depth to defeat allocation-amplification
    // attacks. Real envelope depth is ~5 (envelope -> state -> providers
    // -> claude/codex -> bucket).
    MaxDepth = 16)]
public partial class SnapshotJsonContext : JsonSerializerContext;
