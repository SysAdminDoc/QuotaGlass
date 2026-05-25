using System.Runtime.Versioning;
using System.Windows.Threading;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Evaluates the configured alarm ladder + zero-state rules against each
/// incoming snapshot and fires toasts via <see cref="ToastService"/>. Uses
/// fire-once idempotency keys persisted in <see cref="FiredRulesStore"/>
/// so a single reset window never produces duplicate toasts.
///
/// Tier set is configurable; defaults to the brief's ladder:
///   24h / 12h / 6h / 3h / 1h / 30m / 15m / 5m / at-reset (R1 series)
///
/// Additional rule families:
///   R2 — renewal-arrived (bucket.resetISO advanced AND prior percent > 0)
///   R3 — zero-state (bucket.percentUsed flipped to >= 100)
///   U1 — usage thresholds at 75 / 90 / 95 (one-shot per reset window)
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class AlarmScheduler
{
    /// <summary>
    /// Time-before-reset thresholds, largest first. Tick handler walks the
    /// list and fires the first un-fired tier whose lead time has elapsed.
    /// </summary>
    public static readonly TimeSpan[] DefaultLadder =
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

    public static readonly double[] DefaultThresholds = { 75, 90, 95 };

    private readonly ToastService _toast;
    private readonly FiredRulesStore _fired;
    private readonly DispatcherTimer _tick;

    private SnapshotMessage? _latest;
    private readonly Dictionary<string, double> _lastPercentByBucket = new();
    private readonly Dictionary<string, DateTimeOffset?> _lastResetByBucket = new();

    public TimeSpan[] Ladder { get; set; } = DefaultLadder;
    public double[] UsageThresholds { get; set; } = DefaultThresholds;
    public bool Enabled { get; set; } = true;
    public string? CustomWavPath { get; set; }
    public string? ResetWavPath { get; set; }
    public string? ZeroStateWavPath { get; set; }

    public AlarmScheduler(Dispatcher dispatcher, ToastService toast, FiredRulesStore fired)
    {
        _toast = toast;
        _fired = fired;

        // 15 s polling — fine-grained enough that the 5m / at-reset tiers
        // fire promptly, cheap enough to ignore.
        _tick = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _tick.Tick += (_, _) => Evaluate();
    }

    public void Start() => _tick.Start();

    public void Stop() => _tick.Stop();

    public void OnSnapshot(SnapshotMessage message)
    {
        _latest = message;
        Evaluate();
    }

    private void Evaluate()
    {
        if (!Enabled) return;
        var state = _latest?.State;
        if (state is null) return;

        var now = DateTimeOffset.UtcNow;
        EvaluateProvider("claude", state.Providers.Claude, now);
        EvaluateProvider("codex", state.Providers.Codex, now);
    }

    private void EvaluateProvider(string providerKey, ProviderSnapshot? provider, DateTimeOffset now)
    {
        if (provider is null || !provider.Ok) return;

        foreach (var bucket in provider.Buckets)
        {
            if (string.IsNullOrEmpty(bucket.Id)) continue;

            _lastPercentByBucket.TryGetValue(bucket.Id, out var prevPercent);
            _lastResetByBucket.TryGetValue(bucket.Id, out var prevReset);

            // R2 — renewal-arrived. Detect by resetISO advancing AND prior
            // utilization > 0 (avoids firing on first-ever snapshot).
            if (prevReset.HasValue && bucket.ResetIso.HasValue
                && bucket.ResetIso.Value > prevReset.Value
                && prevPercent > 0 && bucket.PercentUsed < 10)
            {
                var key = $"{providerKey}-{bucket.Id}-R2-{Iso(bucket.ResetIso)}";
                FireOnce(key, $"{Human(providerKey)} {Human(bucket)} renewed",
                    "Fresh quota available.", ResetWavPath);
            }

            // R3 — zero-state. Bucket reached or crossed 100%.
            if (bucket.PercentUsed >= 100)
            {
                var key = $"{providerKey}-{bucket.Id}-R3-{Iso(bucket.ResetIso)}";
                FireOnce(key, $"{Human(providerKey)} {Human(bucket)} at 100%",
                    $"Window exhausted. Resets {HumanReset(bucket)}.", ZeroStateWavPath);
            }

            // U1 — threshold warnings (75 / 90 / 95).
            foreach (var threshold in UsageThresholds)
            {
                if (bucket.PercentUsed >= threshold)
                {
                    var key = $"{providerKey}-{bucket.Id}-U1-{threshold:0}-{Iso(bucket.ResetIso)}";
                    FireOnce(key, $"{Human(providerKey)} {Human(bucket)} at {bucket.PercentUsed:0}%",
                        $"Threshold {threshold:0}% reached. Resets {HumanReset(bucket)}.", CustomWavPath);
                }
            }

            // R1 — imminent-reset ladder. Walk biggest-first, fire the first
            // un-fired tier whose lead time has elapsed.
            if (bucket.ResetIso.HasValue)
            {
                var resetAt = bucket.ResetIso.Value;
                foreach (var lead in Ladder)
                {
                    var fireAt = resetAt - lead;
                    if (now < fireAt) continue;
                    if (now > resetAt + TimeSpan.FromMinutes(2)) continue; // past the window
                    var key = $"{providerKey}-{bucket.Id}-R1-{FormatLead(lead)}-{Iso(bucket.ResetIso)}";
                    if (_fired.HasFired(key)) continue;
                    var title = lead == TimeSpan.Zero
                        ? $"{Human(providerKey)} {Human(bucket)} resetting now"
                        : $"{Human(providerKey)} {Human(bucket)} resets in {HumanLead(lead)}";
                    var body = $"Currently {bucket.PercentUsed:0}% used.";
                    FireOnce(key, title, body, CustomWavPath);
                    break;
                }
            }

            _lastPercentByBucket[bucket.Id] = bucket.PercentUsed;
            _lastResetByBucket[bucket.Id] = bucket.ResetIso;
        }
    }

    private void FireOnce(string key, string title, string body, string? wav)
    {
        if (_fired.HasFired(key)) return;
        _toast.Show(title, body, wav, tag: key);
        _fired.MarkFired(key);
    }

    private static string Iso(DateTimeOffset? dt) => dt?.ToUniversalTime().ToString("O") ?? "no-reset";

    private static string Human(string providerKey) => providerKey switch
    {
        "claude" => "Claude",
        "codex" => "Codex",
        _ => providerKey,
    };

    private static string Human(Bucket b) => b.Kind switch
    {
        "session" => "session",
        "5h" => "5-hour limit",
        "weekly" => b.Model == "all" ? "weekly" : $"{b.Model} weekly",
        _ => b.Label,
    };

    private static string HumanReset(Bucket b)
    {
        if (!b.ResetIso.HasValue) return b.RawResetText ?? "soon";
        var local = b.ResetIso.Value.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("t")
            : local.ToString("ddd h:mm tt");
    }

    private static string HumanLead(TimeSpan lead)
    {
        if (lead.TotalDays >= 1) return $"{(int)lead.TotalDays} day{(lead.TotalDays >= 2 ? "s" : string.Empty)}";
        if (lead.TotalHours >= 1) return $"{(int)lead.TotalHours} hour{(lead.TotalHours >= 2 ? "s" : string.Empty)}";
        return $"{(int)lead.TotalMinutes} min";
    }

    private static string FormatLead(TimeSpan lead)
    {
        if (lead == TimeSpan.Zero) return "0";
        if (lead.TotalDays >= 1) return $"{(int)lead.TotalDays}d";
        if (lead.TotalHours >= 1) return $"{(int)lead.TotalHours}h";
        return $"{(int)lead.TotalMinutes}m";
    }
}
