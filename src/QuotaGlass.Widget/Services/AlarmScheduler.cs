using System.Diagnostics;
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
    private readonly PaceCalculator _pace = new();

    private SnapshotMessage? _latest;
    private readonly Dictionary<string, double> _lastPercentByBucket = new();
    private readonly Dictionary<string, DateTimeOffset?> _lastResetByBucket = new();

    public TimeSpan[] Ladder { get; set; } = DefaultLadder;
    public double[] UsageThresholds { get; set; } = DefaultThresholds;
    public bool Enabled { get; set; } = true;
    /// <summary>R3-P2-06 — per-bucket snooze map. Buckets with a future
    /// snooze instant skip ALL rule families (R1/R2/R3/U1/U2).</summary>
    public Dictionary<string, DateTimeOffset> SnoozedUntil { get; set; } = new();
    /// <summary>R3-P1-03 — when true, fire a U2 pace toast when the
    /// forecasted exhaustion is more than 1× lead-minutes inside the next
    /// R1 tier window.</summary>
    public bool PaceEnabled { get; set; } = true;
    /// <summary>R3-P2-04 — when true, suppress non-priority toasts during
    /// Windows Focus Assist / DND / fullscreen game / presentation mode.
    /// Suppressed keys are still marked fired so they don't fire later.</summary>
    public bool RespectFocusAssist { get; set; } = true;
    public string? CustomWavPath { get; set; }
    public string? ResetWavPath { get; set; }
    public string? ZeroStateWavPath { get; set; }

    /// <summary>F-N7 — optional shell command launched on each fire with
    /// QG_* env vars. Empty disables.</summary>
    public string? WebhookCommand { get; set; }

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

            // R3-P2-06 — skip every rule family for snoozed buckets.
            if (SnoozedUntil.TryGetValue(bucket.Id, out var snoozedUntil)
                && snoozedUntil > now)
            {
                continue;
            }

            _lastPercentByBucket.TryGetValue(bucket.Id, out var prevPercent);
            _lastResetByBucket.TryGetValue(bucket.Id, out var prevReset);

            // R2 — renewal-arrived. Detect by resetISO advancing AND a real
            // drop in utilization (avoids firing on first-ever snapshot and
            // tolerates the user already burning 10%+ in the first minute
            // after reset).
            if (prevReset.HasValue && bucket.ResetIso.HasValue
                && bucket.ResetIso.Value > prevReset.Value
                && prevPercent > 25 && bucket.PercentUsed < prevPercent - 25)
            {
                var key = $"{providerKey}-{bucket.Id}-R2-{Iso(bucket.ResetIso)}";
                FireOnce(key, $"{Human(providerKey)} {Human(bucket)} renewed",
                    "Fresh quota available.", ResetWavPath, providerKey, bucket, "R2");
            }

            // R3 — zero-state. Bucket reached or crossed 100%.
            if (bucket.PercentUsed >= 100)
            {
                var key = $"{providerKey}-{bucket.Id}-R3-{Iso(bucket.ResetIso)}";
                FireOnce(key, $"{Human(providerKey)} {Human(bucket)} at 100%",
                    $"Window exhausted. Resets {HumanReset(bucket)}.", ZeroStateWavPath,
                    providerKey, bucket, "R3");
            }

            // U1 — threshold warnings (75 / 90 / 95).
            foreach (var threshold in UsageThresholds)
            {
                if (bucket.PercentUsed >= threshold)
                {
                    var key = $"{providerKey}-{bucket.Id}-U1-{threshold:0}-{Iso(bucket.ResetIso)}";
                    FireOnce(key, $"{Human(providerKey)} {Human(bucket)} at {bucket.PercentUsed:0}%",
                        $"Threshold {threshold:0}% reached. Resets {HumanReset(bucket)}.", CustomWavPath,
                        providerKey, bucket, $"U1-{threshold:0}");
                }
            }

            // U2 — pace forecast. Fires once per resetISO when the
            // PaceCalculator predicts the bucket will hit 100% before reset.
            // The PaceCalculator returns a forecast string only when burn
            // would exhaust the window; nothing else to threshold against.
            if (PaceEnabled && bucket.PercentUsed < 80)
            {
                var paceLabel = _pace.Forecast(bucket.Id, bucket.PercentUsed, now, bucket.ResetIso);
                if (!string.IsNullOrEmpty(paceLabel))
                {
                    var paceKey = $"{providerKey}-{bucket.Id}-U2-{Iso(bucket.ResetIso)}";
                    FireOnce(paceKey, $"{Human(providerKey)} {Human(bucket)} pace warning",
                        $"{paceLabel}. Currently {bucket.PercentUsed:0}% used.", CustomWavPath,
                        providerKey, bucket, "U2");
                }
            }

            // R1 — imminent-reset ladder. Decision logic extracted into
            // QuotaGlass.Shared.LadderEvaluator so it can be unit-tested
            // without WPF deps.
            if (bucket.ResetIso.HasValue && bucket.ResetIso.Value > DateTimeOffset.MinValue)
            {
                var resetAt = bucket.ResetIso.Value;
                string KeyFor(TimeSpan lead) =>
                    $"{providerKey}-{bucket.Id}-R1-{FormatLead(lead)}-{Iso(bucket.ResetIso)}";

                var decision = LadderEvaluator.Evaluate(
                    Ladder, resetAt, now, lead => _fired.HasFired(KeyFor(lead)));

                if (decision.FireLead.HasValue)
                {
                    var lead = decision.FireLead.Value;
                    var key = KeyFor(lead);
                    var title = lead == TimeSpan.Zero
                        ? $"{Human(providerKey)} {Human(bucket)} resetting now"
                        : $"{Human(providerKey)} {Human(bucket)} resets in {HumanLead(lead)}";
                    var body = $"Currently {bucket.PercentUsed:0}% used.";
                    FireOnce(key, title, body, CustomWavPath, providerKey, bucket, $"R1-{FormatLead(lead)}");

                    foreach (var staleLead in decision.SuppressLeads)
                    {
                        _fired.MarkFired(KeyFor(staleLead));
                    }
                }
            }

            _lastPercentByBucket[bucket.Id] = bucket.PercentUsed;
            _lastResetByBucket[bucket.Id] = bucket.ResetIso;
        }
    }

    private void FireOnce(string key, string title, string body, string? wav,
        string providerKey = "", Bucket? bucket = null, string tier = "")
    {
        if (_fired.HasFired(key)) return;

        // R3-P2-04 — DND-equivalent states swallow the toast but we still
        // mark the key fired so we don't backfire after the user leaves
        // Focus Assist. Matches the R1 ladder cold-start fix semantics.
        if (RespectFocusAssist && FocusAssist.ShouldSuppressToasts())
        {
            _fired.MarkFired(key);
            return;
        }

        _toast.Show(title, body, wav, tag: key);
        _fired.MarkFired(key);

        // F-N7 — fire-and-forget webhook with QG_* env vars. Process is
        // launched via cmd /c so users can write `curl -X POST ntfy/…` or
        // similar without writing batch files. 5-second self-kill prevents
        // a runaway command from leaking processes.
        TryRunWebhook(providerKey, bucket, tier);
    }

    private void TryRunWebhook(string providerKey, Bucket? bucket, string tier)
    {
        if (string.IsNullOrWhiteSpace(WebhookCommand) || bucket is null) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + WebhookCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            // Env vars are the safe injection surface — never substitute into
            // the command string. Power-user pattern: cmd "/c curl -d $env:QG_PERCENT ..."
            psi.Environment["QG_PROVIDER"] = providerKey;
            psi.Environment["QG_BUCKET_ID"] = bucket.Id ?? string.Empty;
            psi.Environment["QG_PERCENT"] = bucket.PercentUsed.ToString("0.##");
            psi.Environment["QG_RESET_ISO"] = bucket.ResetIso?.ToString("O") ?? string.Empty;
            psi.Environment["QG_TIER"] = tier;

            var proc = Process.Start(psi);
            if (proc is null) return;
            // Fire-and-forget; reap after 5 s so we never leak processes.
            _ = Task.Run(() =>
            {
                try
                {
                    if (!proc.WaitForExit(5000))
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                    }
                }
                finally
                {
                    proc.Dispose();
                }
            });
        }
        catch
        {
            // Webhook failure must never break the alarm UX.
        }
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
