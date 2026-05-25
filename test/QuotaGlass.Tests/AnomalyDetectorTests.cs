using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// L-09 — exercises the spike heuristic. The detector should be permissive
/// during normal growth and fire when a single sample is ≥3× the median of
/// recent positive deltas (and ≥5 percentage points in absolute terms).
/// </summary>
public sealed class AnomalyDetectorTests
{
    private static HistorySample Sample(int minuteOffset, double percent) => new()
    {
        Ts = new DateTimeOffset(2026, 6, 1, 12, minuteOffset, 0, TimeSpan.Zero),
        PercentUsed = percent,
    };

    [Fact]
    public void Sub_window_size_returns_null()
    {
        var samples = new[] { Sample(0, 10), Sample(5, 15), Sample(10, 20) };
        Assert.Null(AnomalyDetector.DetectSpike(samples));
    }

    [Fact]
    public void Steady_growth_does_not_spike()
    {
        // Linear: every step grows by 2 — median delta = 2, latest = 2.
        var samples = new[]
        {
            Sample(0, 10), Sample(5, 12), Sample(10, 14),
            Sample(15, 16), Sample(20, 18), Sample(25, 20),
        };
        Assert.Null(AnomalyDetector.DetectSpike(samples));
    }

    [Fact]
    public void Sudden_burst_triggers_spike()
    {
        // Baseline deltas: 1, 1, 1, 1; latest jump: 12.
        var samples = new[]
        {
            Sample(0, 10), Sample(5, 11), Sample(10, 12),
            Sample(15, 13), Sample(20, 14), Sample(25, 26),
        };
        var spike = AnomalyDetector.DetectSpike(samples);
        Assert.NotNull(spike);
        Assert.Equal(26, spike!.PercentUsed);
    }

    [Fact]
    public void Small_jump_below_absolute_threshold_does_not_spike()
    {
        // Latest delta 4 (< MinAbsoluteDelta = 5).
        var samples = new[]
        {
            Sample(0, 10), Sample(5, 11), Sample(10, 11.5),
            Sample(15, 12), Sample(20, 12.5), Sample(25, 16.5),
        };
        // 4 < 5 → no spike despite high multiplier.
        Assert.Null(AnomalyDetector.DetectSpike(samples));
    }

    [Fact]
    public void Reset_drop_does_not_count_as_spike()
    {
        var samples = new[]
        {
            Sample(0, 80), Sample(5, 82), Sample(10, 5),
            Sample(15, 7), Sample(20, 9), Sample(25, 11),
        };
        // Last delta is 2 → far below absolute threshold.
        Assert.Null(AnomalyDetector.DetectSpike(samples));
    }
}
