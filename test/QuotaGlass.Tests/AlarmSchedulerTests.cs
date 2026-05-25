using System.Windows.Threading;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class AlarmSchedulerTests : IDisposable
{
    private readonly string _firedPath = Path.Combine(Path.GetTempPath(),
        $"qg-alarm-tests-{Guid.NewGuid():N}.json");
    private readonly DateTimeOffset _now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { File.Delete(_firedPath); } catch { }
    }

    [Fact]
    public void R1_ladder_fires_once_for_same_reset_window()
    {
        var toast = new RecordingToastService();
        var fired = new FiredRulesStore(_firedPath);
        var scheduler = NewScheduler(toast, fired);
        var resetAt = _now + TimeSpan.FromMinutes(5);

        scheduler.OnSnapshot(Snapshot(Bucket("bucket-r1", percent: 12, resetAt)));
        scheduler.OnSnapshot(Snapshot(Bucket("bucket-r1", percent: 12, resetAt)));

        Assert.Single(toast.Shown);
        Assert.Contains("resets in 5 min", toast.Shown[0].Title);
        Assert.True(fired.HasFired(Key("R1-5m", "bucket-r1", resetAt)));
    }

    [Fact]
    public void Snoozed_bucket_suppresses_every_rule_family_without_marking_fired()
    {
        var toast = new RecordingToastService();
        var fired = new FiredRulesStore(_firedPath);
        var scheduler = NewScheduler(toast, fired);
        var resetAt = _now + TimeSpan.FromMinutes(5);
        scheduler.SnoozedUntil["bucket-snoozed"] = _now + TimeSpan.FromHours(1);

        scheduler.OnSnapshot(Snapshot(Bucket("bucket-snoozed", percent: 100, resetAt)));

        Assert.Empty(toast.Shown);
        Assert.False(fired.HasFired(Key("R1-5m", "bucket-snoozed", resetAt)));
        Assert.False(fired.HasFired(Key("R3", "bucket-snoozed", resetAt)));
    }

    [Fact]
    public void Focus_assist_suppression_marks_key_fired_without_showing_toast()
    {
        var toast = new RecordingToastService();
        var fired = new FiredRulesStore(_firedPath);
        var suppress = true;
        var scheduler = NewScheduler(toast, fired, shouldSuppressToasts: () => suppress);
        var resetAt = _now + TimeSpan.FromMinutes(5);

        scheduler.OnSnapshot(Snapshot(Bucket("bucket-focus", percent: 100, resetAt)));
        suppress = false;
        scheduler.OnSnapshot(Snapshot(Bucket("bucket-focus", percent: 100, resetAt)));

        Assert.Empty(toast.Shown);
        Assert.True(fired.HasFired(Key("R3", "bucket-focus", resetAt)));
    }

    [Fact]
    public void U3_spike_at_exhaustion_suppresses_R3_zero_state_toast()
    {
        var toast = new RecordingToastService();
        var fired = new FiredRulesStore(_firedPath);
        var scheduler = NewScheduler(toast, fired);
        scheduler.AnomalyDetectionEnabled = true;
        scheduler.Ladder = Array.Empty<TimeSpan>();
        var resetAt = _now + TimeSpan.FromMinutes(30);
        var bucket = Bucket("bucket-u3", percent: 100, resetAt);
        scheduler.UpdateHistory(bucket.Id, SpikeHistory(bucket.Id, resetAt));

        scheduler.OnSnapshot(Snapshot(bucket));

        Assert.Single(toast.Shown);
        Assert.Contains("usage spike", toast.Shown[0].Title);
        Assert.True(fired.HasFired(Key("U3", bucket.Id, resetAt)));
        Assert.True(fired.HasFired(Key("R3", bucket.Id, resetAt)));
    }

    private AlarmScheduler NewScheduler(
        IToastService toast,
        FiredRulesStore fired,
        Func<bool>? shouldSuppressToasts = null)
    {
        return new AlarmScheduler(
            Dispatcher.CurrentDispatcher,
            toast,
            fired,
            utcNow: () => _now,
            shouldSuppressToasts: shouldSuppressToasts ?? (() => false))
        {
            UsageThresholds = Array.Empty<double>(),
            PaceEnabled = false,
            AnomalyDetectionEnabled = false,
            RespectFocusAssist = true,
        };
    }

    private static SnapshotMessage Snapshot(Bucket bucket) => new()
    {
        Kind = "snapshot",
        SchemaVersion = SchemaVersion.Current,
        Timestamp = DateTimeOffset.UtcNow,
        State = new ExtensionState
        {
            FetchedAtIso = DateTimeOffset.UtcNow,
            Providers = new ProviderMap
            {
                Claude = new ProviderSnapshot
                {
                    Ok = true,
                    Provider = "claude",
                    Buckets = { bucket },
                },
            },
        },
    };

    private static Bucket Bucket(string id, double percent, DateTimeOffset resetAt) => new()
    {
        Id = id,
        Kind = "session",
        Label = "Session",
        PercentUsed = percent,
        ResetIso = resetAt,
    };

    private static List<HistorySample> SpikeHistory(string bucketId, DateTimeOffset resetAt)
    {
        var start = resetAt - TimeSpan.FromHours(1);
        return new List<HistorySample>
        {
            Sample(start, 10),
            Sample(start + TimeSpan.FromMinutes(5), 11),
            Sample(start + TimeSpan.FromMinutes(10), 12),
            Sample(start + TimeSpan.FromMinutes(15), 13),
            Sample(start + TimeSpan.FromMinutes(20), 14),
            Sample(start + TimeSpan.FromMinutes(25), 100),
        };
    }

    private static HistorySample Sample(DateTimeOffset ts, double percent) => new()
    {
        Ts = ts,
        PercentUsed = percent,
    };

    private static string Key(string tier, string bucketId, DateTimeOffset resetAt) =>
        $"claude-{bucketId}-{tier}-{resetAt.ToUniversalTime():O}";

    private sealed class RecordingToastService : IToastService
    {
        public List<ShownToast> Shown { get; } = new();

        public void Show(
            string title,
            string? body = null,
            string? customWavPath = null,
            string? tag = null,
            IReadOnlyList<ToastAction>? actions = null)
        {
            Shown.Add(new ShownToast(title, body, tag, actions));
        }
    }

    private sealed record ShownToast(
        string Title,
        string? Body,
        string? Tag,
        IReadOnlyList<ToastAction>? Actions);
}
