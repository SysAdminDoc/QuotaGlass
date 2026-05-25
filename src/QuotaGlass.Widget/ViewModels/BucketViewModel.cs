using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;

namespace QuotaGlass.Widget.ViewModels;

public sealed class BucketViewModel : INotifyPropertyChanged
{
    private Bucket _model = new();
    private string _providerKey = string.Empty;
    private string? _cachedTimeUntilResetLabel;
    private string? _paceLabel;
    private double _staleOpacity = 1.0;
    private IReadOnlyList<HistorySample> _sparkline = Array.Empty<HistorySample>();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable identifier the widget reconciles on (see F-A5).</summary>
    public string Id => _model.Id;

    /// <summary>Display-only label; may drift between extension versions.</summary>
    public string Label => _model.Label;

    public string Kind => _model.Kind;

    public string? Model => _model.Model;

    /// <summary>Human-readable provider name from the snapshot envelope.</summary>
    public string Provider => HumanProvider(_providerKey);

    public double Percent => Math.Clamp(_model.PercentUsed, 0, 100);

    public string PercentLabel => $"{Percent:0}% used";

    public string TimeUntilResetLabel
    {
        get
        {
            if (_model.ResetIso is null) return _model.RawResetText ?? "—";
            var delta = _model.ResetIso.Value - DateTimeOffset.UtcNow;
            if (delta.TotalSeconds <= 0) return "renewed";
            if (delta.TotalDays >= 1) return $"{(int)delta.TotalDays}d {delta.Hours}h";
            if (delta.TotalHours >= 1) return $"{(int)delta.TotalHours}h {delta.Minutes}m";
            if (delta.TotalMinutes >= 1) return $"{(int)delta.TotalMinutes}m {delta.Seconds}s";
            return $"{(int)delta.TotalSeconds}s";
        }
    }

    public string ResetAtLabel
    {
        get
        {
            if (_model.ResetIso is null) return _model.RawResetText ?? "Reset time unknown";
            var local = _model.ResetIso.Value.ToLocalTime();
            var sameDay = local.Date == DateTimeOffset.Now.Date;
            return sameDay
                ? $"Resets at {local:t}"
                : $"Resets {local:ddd h:mm tt}";
        }
    }

    public string KindBadge => _model.Kind switch
    {
        "session" => "session",
        "5h" => "5-hour",
        "weekly" => "weekly",
        _ => _model.Kind,
    };

    /// <summary>
    /// Provider URL for the click-to-open-analytics affordance.
    /// </summary>
    public string AnalyticsUrl => _providerKey switch
    {
        "claude" => "https://claude.ai/settings/usage",
        "codex" => "https://chatgpt.com/codex/cloud/settings/analytics#usage",
        _ => "",
    };

    /// <summary>
    /// Burn-rate forecast text. Empty when no pace estimate available.
    /// </summary>
    public string PaceLabel => _paceLabel ?? string.Empty;

    public bool HasPace => !string.IsNullOrEmpty(_paceLabel);

    /// <summary>
    /// Multiline tooltip body for the ring-hover affordance (NX-09). Includes
    /// the human-readable plan, source, error (if any), and the resetISO in
    /// the user's local time.
    /// </summary>
    public string HoverTooltip
    {
        get
        {
            var parts = new List<string>
            {
                $"{Provider} — {Label}",
                $"{PercentLabel}",
                ResetAtLabel,
            };
            if (!string.IsNullOrEmpty(KindBadge)) parts.Add($"({KindBadge})");
            return string.Join("\n", parts);
        }
    }

    /// <summary>
    /// 0..1 dim factor applied to the ring when data is stale. 1.0 = fresh,
    /// 0.5 = stale.
    /// </summary>
    public double StaleOpacity => _staleOpacity;

    public void SetPace(string? label)
    {
        if (_paceLabel == label) return;
        _paceLabel = label;
        Raise(nameof(PaceLabel));
        Raise(nameof(HasPace));
        Raise(nameof(PaceMarkerPercent));
    }

    /// <summary>
    /// L-08 — projected percent-at-reset based on the two most recent
    /// history samples. NaN when we can't or shouldn't draw a tick.
    /// </summary>
    public double PaceMarkerPercent
    {
        get
        {
            if (string.IsNullOrEmpty(_paceLabel)) return double.NaN;
            if (_sparkline is null || _sparkline.Count < 2) return double.NaN;
            if (_model.ResetIso is null) return double.NaN;

            var prev = _sparkline[^2];
            var last = _sparkline[^1];
            var dtMinutes = (last.Ts - prev.Ts).TotalMinutes;
            if (dtMinutes <= 0) return double.NaN;
            var slope = (last.PercentUsed - prev.PercentUsed) / dtMinutes;
            if (slope <= 0) return double.NaN;

            var remainingMinutes = (_model.ResetIso.Value - last.Ts).TotalMinutes;
            if (remainingMinutes <= 0) return double.NaN;

            var projected = last.PercentUsed + slope * remainingMinutes;
            return Math.Clamp(projected, _model.PercentUsed, 100);
        }
    }

    /// <summary>R3-P2-02 / NX-08 — durable history samples for the sparkline.</summary>
    public IReadOnlyList<HistorySample> SparklineData => _sparkline;

    public bool HasSparkline => _sparkline.Count >= 2;

    public void SetSparklineData(IReadOnlyList<HistorySample> samples)
    {
        _sparkline = samples ?? Array.Empty<HistorySample>();
        Raise(nameof(SparklineData));
        Raise(nameof(HasSparkline));
    }

    public void SetStale(double opacity)
    {
        if (Math.Abs(_staleOpacity - opacity) < 0.01) return;
        _staleOpacity = opacity;
        Raise(nameof(StaleOpacity));
    }

    public void Apply(string providerKey, Bucket bucket)
    {
        _providerKey = providerKey;
        _model = bucket;
        _cachedTimeUntilResetLabel = null;
        Raise(nameof(Id));
        Raise(nameof(Label));
        Raise(nameof(Kind));
        Raise(nameof(Model));
        Raise(nameof(Provider));
        Raise(nameof(Percent));
        Raise(nameof(PercentLabel));
        Raise(nameof(TimeUntilResetLabel));
        Raise(nameof(ResetAtLabel));
        Raise(nameof(KindBadge));
        Raise(nameof(AnalyticsUrl));
    }

    public void TickCountdown()
    {
        // Avoid 1 Hz INPC storm when the formatted string ("3h 5m") changes
        // only once per minute. Recompute, compare, raise only on change.
        var next = TimeUntilResetLabel;
        if (next == _cachedTimeUntilResetLabel) return;
        _cachedTimeUntilResetLabel = next;
        Raise(nameof(TimeUntilResetLabel));
    }

    private static string HumanProvider(string key) => key switch
    {
        "claude" => "Claude",
        "codex" => "Codex",
        "" => "—",
        _ => char.ToUpperInvariant(key[0]) + key[1..],
    };

    private void Raise([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
