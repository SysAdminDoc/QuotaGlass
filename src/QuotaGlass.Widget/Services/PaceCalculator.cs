using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Lightweight burn-rate estimator. Mirrors the gist of
/// AI-Usage_Tracker/src/lib/history.js#forecastExhaustion — linear
/// extrapolation between two snapshots — but works on just the two most
/// recent samples instead of a 48 h window. Good enough for a footer.
///
/// Returns text like "Pace: 100% by 4:32 PM" or "Pace: 100% in 2h 15m".
/// </summary>
public sealed class PaceCalculator
{
    private readonly Dictionary<string, (DateTimeOffset Ts, double Percent)> _previous = new();

    public string? Forecast(string bucketId, double currentPercent, DateTimeOffset now, DateTimeOffset? resetIso)
    {
        if (_previous.TryGetValue(bucketId, out var prev))
        {
            try
            {
                var dt = (now - prev.Ts).TotalMinutes;
                var dPercent = currentPercent - prev.Percent;
                _previous[bucketId] = (now, currentPercent);

                if (dt < 1) return null;                  // too soon to estimate
                if (dPercent <= 0.5) return null;          // not burning fast enough
                if (currentPercent >= 100) return null;    // already exhausted

                var minutesToHundred = (100 - currentPercent) / dPercent * dt;
                if (double.IsNaN(minutesToHundred) || double.IsInfinity(minutesToHundred)) return null;
                if (minutesToHundred <= 0) return null;

                var eta = now.AddMinutes(minutesToHundred);
                // Only show if pace would exhaust BEFORE the reset.
                if (resetIso.HasValue && eta >= resetIso.Value) return null;

                var local = eta.ToLocalTime();
                return local.Date == DateTimeOffset.Now.Date
                    ? $"Pace: 100% by {local:t}"
                    : $"Pace: 100% {local:ddd h:mm tt}";
            }
            catch
            {
                return null;
            }
        }

        _previous[bucketId] = (now, currentPercent);
        return null;
    }
}
