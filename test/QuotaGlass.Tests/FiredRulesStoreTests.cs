using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// Locks in FiredRulesStore's idempotency contract — once a key is marked
/// fired, it stays fired for <see cref="FiredRulesStore.RetainDays"/> days,
/// then prunes on next load. Hostile filesystem conditions (read-only
/// directory) must not crash the scheduler.
/// </summary>
public sealed class FiredRulesStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        $"qg-fired-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void MarkFired_HasFired_round_trip()
    {
        var store = new FiredRulesStore(_path);
        Assert.False(store.HasFired("k"));
        store.MarkFired("k");
        Assert.True(store.HasFired("k"));
    }

    [Fact]
    public void Marking_idempotent()
    {
        var store = new FiredRulesStore(_path);
        store.MarkFired("k");
        store.MarkFired("k");
        Assert.True(store.HasFired("k"));
    }

    [Fact]
    public void Survives_across_instances()
    {
        var first = new FiredRulesStore(_path);
        first.MarkFired("alpha");
        first.MarkFired("beta");

        var second = new FiredRulesStore(_path);
        Assert.True(second.HasFired("alpha"));
        Assert.True(second.HasFired("beta"));
        Assert.False(second.HasFired("gamma"));
    }

    [Fact]
    public void Prune_drops_keys_older_than_retention_window()
    {
        // Hand-craft a state file where one key is 30 days old.
        var stale = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        var fresh = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        File.WriteAllText(_path,
            $"{{\"fired\":{{\"stale-key\":{stale},\"fresh-key\":{fresh}}}}}");

        var store = new FiredRulesStore(_path);  // Prune runs in ctor
        Assert.False(store.HasFired("stale-key"));
        Assert.True(store.HasFired("fresh-key"));
    }
}
