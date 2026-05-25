namespace QuotaGlass.Shared;

/// <summary>
/// Pure decision logic for the R1 imminent-reset ladder. Lives in the
/// Shared project so it can be unit-tested without taking a dependency
/// on WPF / WinRT.
///
/// The contract: given a sorted-smallest-first ladder and a predicate
/// reporting which tier keys have already fired for the current
/// resetISO, return (a) the smallest un-fired lead whose fireAt has
/// elapsed, and (b) every larger-lead tier whose fireAt also elapsed
/// but which has no key yet — those should be marked-fired by the
/// caller so they don't backfire stale toasts at the 15 s tick rate.
/// </summary>
public static class LadderEvaluator
{
    public sealed record Decision(TimeSpan? FireLead, IReadOnlyList<TimeSpan> SuppressLeads);

    public static Decision Evaluate(
        IEnumerable<TimeSpan> ladder,
        DateTimeOffset resetAt,
        DateTimeOffset now,
        Func<TimeSpan, bool> hasFired,
        TimeSpan? windowGrace = null)
    {
        var grace = windowGrace ?? TimeSpan.FromMinutes(2);
        if (now > resetAt + grace) return new Decision(null, Array.Empty<TimeSpan>());

        var sorted = ladder.OrderBy(t => t).ToArray();
        TimeSpan? fireLead = null;
        foreach (var lead in sorted)
        {
            var fireAt = resetAt - lead;
            if (now < fireAt) continue;
            if (hasFired(lead)) continue;
            fireLead = lead;
            break;
        }

        if (!fireLead.HasValue) return new Decision(null, Array.Empty<TimeSpan>());

        var suppress = new List<TimeSpan>();
        foreach (var staleLead in sorted)
        {
            if (staleLead <= fireLead.Value) continue;
            var staleFireAt = resetAt - staleLead;
            if (now < staleFireAt) continue;
            if (!hasFired(staleLead)) suppress.Add(staleLead);
        }

        return new Decision(fireLead, suppress);
    }
}
