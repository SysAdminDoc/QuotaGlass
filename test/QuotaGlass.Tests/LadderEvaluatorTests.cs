using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// Locks in the R3-P0-01 fix: cold start with empty FiredRulesStore must
/// fire the SMALLEST un-fired tier (closest to now), not the biggest, AND
/// must mark every larger missed tier as fired-but-suppressed so the
/// 15 s scheduler tick doesn't cascade stale toasts.
/// </summary>
public sealed class LadderEvaluatorTests
{
    private static readonly TimeSpan[] DefaultLadder =
    {
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(1),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(5),
        TimeSpan.Zero,
    };

    [Fact]
    public void Cold_start_5min_before_reset_fires_5m_tier_and_marks_all_larger_as_suppressed()
    {
        var resetAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var now = resetAt - TimeSpan.FromMinutes(5);

        var decision = LadderEvaluator.Evaluate(DefaultLadder, resetAt, now, _ => false);

        Assert.NotNull(decision.FireLead);
        Assert.Equal(TimeSpan.FromMinutes(5), decision.FireLead);

        // The 24h..15m tiers all have fireAt < now → suppress.
        // 0 (at-reset) has fireAt = resetAt and now < fireAt → NOT suppressed.
        var expectedSuppressed = new[]
        {
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24),
        };
        Assert.Equal(expectedSuppressed, decision.SuppressLeads);
    }

    [Fact]
    public void Walking_each_tick_in_order_fires_each_tier_exactly_once()
    {
        var resetAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var fired = new HashSet<TimeSpan>();
        bool HasFired(TimeSpan lead) => fired.Contains(lead);

        // Simulate the scheduler ticking right at each tier-fireAt boundary.
        foreach (var lead in DefaultLadder.OrderByDescending(t => t))
        {
            var now = resetAt - lead + TimeSpan.FromSeconds(1);
            var decision = LadderEvaluator.Evaluate(DefaultLadder, resetAt, now, HasFired);

            Assert.Equal(lead, decision.FireLead);
            Assert.Empty(decision.SuppressLeads);

            fired.Add(decision.FireLead.Value);
        }
    }

    [Fact]
    public void Past_grace_window_returns_no_decision()
    {
        var resetAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var now = resetAt + TimeSpan.FromHours(2);

        var decision = LadderEvaluator.Evaluate(DefaultLadder, resetAt, now, _ => false);

        Assert.Null(decision.FireLead);
        Assert.Empty(decision.SuppressLeads);
    }

    [Fact]
    public void Already_fired_5m_tier_picks_0_at_reset_when_now_passes_reset()
    {
        var resetAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var fired = new HashSet<TimeSpan> { TimeSpan.FromMinutes(5) };

        var now = resetAt + TimeSpan.FromSeconds(30);
        var decision = LadderEvaluator.Evaluate(DefaultLadder, resetAt, now, fired.Contains);

        Assert.Equal(TimeSpan.Zero, decision.FireLead);
    }

    [Fact]
    public void Before_any_tier_elapses_returns_no_decision()
    {
        var resetAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var now = resetAt - TimeSpan.FromHours(30); // 30h before — past 24h tier? No, 30h > 24h so fireAt is in the future.

        var decision = LadderEvaluator.Evaluate(DefaultLadder, resetAt, now, _ => false);

        Assert.Null(decision.FireLead);
        Assert.Empty(decision.SuppressLeads);
    }

    [Fact]
    public void Window_grace_is_respected()
    {
        var resetAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var now = resetAt + TimeSpan.FromMinutes(5);

        // Default 2-minute grace — should be past.
        var decision = LadderEvaluator.Evaluate(DefaultLadder, resetAt, now, _ => false);
        Assert.Null(decision.FireLead);

        // Custom 10-minute grace — still inside window.
        var decisionInGrace = LadderEvaluator.Evaluate(
            DefaultLadder, resetAt, now, _ => false, windowGrace: TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.Zero, decisionInGrace.FireLead);
    }
}
