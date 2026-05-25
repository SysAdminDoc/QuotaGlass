using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// R4-P1-02 — locks in the multi-source merge contract (extension chain +
/// `--poll-credentials` writes to a sibling file; widget merges both).
/// Rules: newer envelope wins for top-level metadata; per-provider, the
/// fresher OK snapshot wins; OK=false snapshots fall back to the other
/// producer if it's OK.
/// </summary>
public sealed class SnapshotWatcherMergeTests
{
    [Fact]
    public void Merge_returns_null_when_both_inputs_null()
    {
        Assert.Null(SnapshotWatcher.Merge(null, null));
    }

    [Fact]
    public void Merge_returns_other_when_one_input_null()
    {
        var ext = Envelope("ext", DateTimeOffset.UtcNow);
        Assert.Equal(ext, SnapshotWatcher.Merge(ext, null));
        Assert.Equal(ext, SnapshotWatcher.Merge(null, ext));
    }

    [Fact]
    public void Merge_picks_newest_envelope_timestamp()
    {
        var older = Envelope("older", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var newer = Envelope("newer", new DateTimeOffset(2026, 6, 1, 12, 5, 0, TimeSpan.Zero));

        var merged = SnapshotWatcher.Merge(older, newer);
        Assert.NotNull(merged);
        Assert.Equal("newer", merged!.ExtensionVersion);
    }

    [Fact]
    public void Merge_prefers_ok_provider_when_primary_failed()
    {
        var ext = Envelope("ext", new DateTimeOffset(2026, 6, 1, 12, 5, 0, TimeSpan.Zero));
        ext.State!.Providers.Claude = new ProviderSnapshot
        {
            Ok = false, Provider = "claude", Error = "extension-down",
        };

        var creds = Envelope("creds", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        creds.State!.Providers.Claude = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets = { new Bucket { Id = "claude-session", PercentUsed = 42 } },
        };

        var merged = SnapshotWatcher.Merge(ext, creds);
        Assert.NotNull(merged);
        // Extension was newer + primary, but failed; creds (older + OK)
        // fills the gap.
        Assert.True(merged!.State!.Providers.Claude!.Ok);
        Assert.Single(merged.State.Providers.Claude.Buckets);
        Assert.Equal(42, merged.State.Providers.Claude.Buckets[0].PercentUsed);
    }

    [Fact]
    public void Merge_keeps_primary_ok_provider_even_when_secondary_also_ok()
    {
        var ext = Envelope("ext", new DateTimeOffset(2026, 6, 1, 12, 5, 0, TimeSpan.Zero));
        ext.State!.Providers.Claude = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets = { new Bucket { Id = "claude-session", PercentUsed = 88 } },
        };

        var creds = Envelope("creds", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        creds.State!.Providers.Claude = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets = { new Bucket { Id = "claude-session", PercentUsed = 33 } },
        };

        var merged = SnapshotWatcher.Merge(ext, creds);
        Assert.NotNull(merged);
        // Primary (newer) wins on the tie.
        Assert.Equal(88, merged!.State!.Providers.Claude!.Buckets[0].PercentUsed);
    }

    private static SnapshotMessage Envelope(string label, DateTimeOffset ts) => new()
    {
        Kind = "snapshot",
        SchemaVersion = SchemaVersion.Current,
        Timestamp = ts,
        ExtensionVersion = label,
        State = new ExtensionState
        {
            FetchedAtIso = ts,
            Providers = new ProviderMap(),
        },
    };
}
