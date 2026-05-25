using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// Locks in HistoryStore's ring-buffer + dedupe + Schema-v2-merge
/// semantics. R4-P1-01 (debounce-then-flush) is also covered: append
/// without flush leaves no file on disk.
/// </summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        $"qg-history-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void AppendSample_caps_at_max_samples_per_bucket()
    {
        var store = new HistoryStore(_path);
        for (var i = 0; i < HistoryStore.MaxSamplesPerBucket + 8; i++)
        {
            store.AppendSample("b1", new DateTimeOffset(2026, 6, 1, 12, i, 0, TimeSpan.Zero), i);
        }
        store.Flush();

        var read = store.Read("b1");
        Assert.Equal(HistoryStore.MaxSamplesPerBucket, read.Count);
        // Oldest entries dropped first.
        Assert.Equal(8.0, read[0].PercentUsed, precision: 1);
        Assert.Equal(HistoryStore.MaxSamplesPerBucket + 7.0, read[^1].PercentUsed, precision: 1);
    }

    [Fact]
    public void AppendSample_dedupes_by_timestamp()
    {
        var store = new HistoryStore(_path);
        var ts = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        store.AppendSample("b1", ts, 10);
        store.AppendSample("b1", ts, 99); // same ts → ignored
        store.Flush();

        var read = store.Read("b1");
        Assert.Single(read);
        Assert.Equal(10, read[0].PercentUsed);
    }

    [Fact]
    public void Flush_writes_only_after_pending_append()
    {
        var store = new HistoryStore(_path);
        store.Flush(); // no-op when no pending writes
        Assert.False(File.Exists(_path));

        store.AppendSample("b1", DateTimeOffset.UtcNow, 42);
        store.Flush();
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void MergeIncoming_unions_local_and_remote_samples()
    {
        var store = new HistoryStore(_path);
        var ts1 = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var ts2 = new DateTimeOffset(2026, 6, 1, 12, 5, 0, TimeSpan.Zero);
        store.AppendSample("b1", ts1, 10);

        var incoming = new Dictionary<string, List<HistorySample>>
        {
            ["b1"] = new()
            {
                new HistorySample { Ts = ts1, PercentUsed = 999 }, // dup of existing — ignored
                new HistorySample { Ts = ts2, PercentUsed = 20 },
            },
            ["b2"] = new()
            {
                new HistorySample { Ts = ts1, PercentUsed = 50 },
            },
        };
        store.MergeIncoming(incoming);
        store.Flush();

        var b1 = store.Read("b1");
        Assert.Equal(2, b1.Count);
        Assert.Equal(10, b1[0].PercentUsed);   // dedupe preserved local value
        Assert.Equal(20, b1[1].PercentUsed);

        var b2 = store.Read("b2");
        Assert.Single(b2);
        Assert.Equal(50, b2[0].PercentUsed);
    }

    [Fact]
    public void Read_unknown_bucket_returns_empty()
    {
        var store = new HistoryStore(_path);
        Assert.Empty(store.Read("nope"));
    }

    [Fact]
    public void State_persists_across_instances()
    {
        var first = new HistoryStore(_path);
        first.AppendSample("b1", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), 33);
        first.Flush();

        var second = new HistoryStore(_path);
        var read = second.Read("b1");
        Assert.Single(read);
        Assert.Equal(33, read[0].PercentUsed);
    }
}
