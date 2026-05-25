namespace QuotaGlass.Shared;

/// <summary>
/// L-09 — flags a "spike" when the most recent sample's percent jump is
/// more than <see cref="SpikeMultiplier"/>× the median delta of the last
/// <see cref="WindowSize"/> samples. Designed to fire once per resetISO
/// per bucket (caller is responsible for idempotency via FiredRulesStore).
///
/// Pure-functional; lives in Shared so it can be unit-tested without WPF
/// dependencies.
/// </summary>
public static class AnomalyDetector
{
    public const int WindowSize = 6;
    public const double SpikeMultiplier = 3.0;
    public const double MinAbsoluteDelta = 5.0;

    public static HistorySample? DetectSpike(IReadOnlyList<HistorySample> samples)
    {
        if (samples is null || samples.Count < WindowSize) return null;

        var window = new List<HistorySample>(WindowSize);
        for (var i = samples.Count - WindowSize; i < samples.Count; i++)
        {
            window.Add(samples[i]);
        }

        var deltas = new List<double>(WindowSize - 1);
        for (var i = 1; i < window.Count; i++)
        {
            var d = window[i].PercentUsed - window[i - 1].PercentUsed;
            if (d > 0) deltas.Add(d);
        }
        if (deltas.Count < 2) return null;

        var latestDelta = deltas[^1];
        if (latestDelta < MinAbsoluteDelta) return null;

        // Median of the prior deltas (exclude the latest from the baseline
        // so a sudden spike doesn't poison its own threshold).
        var baseline = deltas.Take(deltas.Count - 1).OrderBy(d => d).ToArray();
        var median = baseline[baseline.Length / 2];
        if (median <= 0) return null;

        return latestDelta >= median * SpikeMultiplier ? window[^1] : null;
    }
}
