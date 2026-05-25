using System.Windows.Threading;
using QuotaGlass.Shared;
using QuotaGlass.Widget.Services;
using QuotaGlass.Widget.ViewModels;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class ViewModelRegressionTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"qg-vm-tests-{Guid.NewGuid():N}");

    public ViewModelRegressionTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Fact]
    public void Setup_card_rechecks_dismissal_even_when_health_snapshot_is_unchanged()
    {
        var settings = new SettingsStore(Path.Combine(_tmpDir, "settings.json"));
        settings.Update(s => s.Widget.SetupCardDismissedUntilUtc = DateTimeOffset.UtcNow.AddHours(1));
        var health = new HealthSnapshot(false, false, false, null);
        var vm = new SetupCardViewModel(Dispatcher.CurrentDispatcher, settings, () => health);

        Assert.False(vm.IsVisible);

        settings.Update(s => s.Widget.SetupCardDismissedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1));
        vm.Refresh();

        Assert.True(vm.IsVisible);
    }

    [Fact]
    public void Calendar_rebuild_orders_resets_within_each_day()
    {
        var later = DateTimeOffset.Now.Date.AddHours(18);
        var earlier = DateTimeOffset.Now.Date.AddHours(9);
        var first = BucketView("claude", "later", later);
        var second = BucketView("codex", "earlier", earlier);
        var calendar = new CalendarViewModel();

        calendar.Rebuild(new[] { first, second });

        var day = Assert.Single(calendar.Days);
        Assert.Equal(new[] { "earlier", "later" }, day.Resets.Select(r => r.Label).ToArray());
    }

    [Fact]
    public void Bucket_pace_marker_handles_over_100_percent_without_throwing()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = new BucketViewModel();
        vm.Apply("claude", new Bucket
        {
            Id = "overfull",
            Kind = "session",
            Label = "Session",
            PercentUsed = 150,
            ResetIso = now.AddHours(1),
        });
        vm.SetPace("Forecast exhaustion before reset");
        vm.SetSparklineData(new[]
        {
            new HistorySample { Ts = now.AddMinutes(-10), PercentUsed = 120 },
            new HistorySample { Ts = now, PercentUsed = 130 },
        });

        Assert.Equal(100, vm.Percent);
        Assert.Equal(100, vm.PaceMarkerPercent);
    }

    [Fact]
    public void Reset_to_defaults_preserves_window_state_and_refreshes_all_bound_settings()
    {
        var settings = new SettingsStore(Path.Combine(_tmpDir, "settings-reset.json"));
        var dismissedUntil = DateTimeOffset.UtcNow.AddDays(1);
        settings.Update(s =>
        {
            s.Widget.X = 123;
            s.Widget.Y = 456;
            s.Widget.Autostart = false;
            s.Widget.HasShownFirstRunToast = true;
            s.Widget.SetupCardDismissedUntilUtc = dismissedUntil;
            s.Alarms.WebhookCommand = "curl https://example.invalid";
            s.Alarms.PaceEnabled = false;
            s.Alarms.RespectFocusAssist = false;
            s.Alarms.ResetWavPath = @"C:\reset.wav";
            s.Alarms.ZeroStateWavPath = @"C:\zero.wav";
            s.Display.Theme = ThemeService.ThemeLatte;
            s.RefreshMinutes = 99;
        });

        var vm = new SettingsPanelViewModel(settings);
        var raised = new HashSet<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName)) raised.Add(e.PropertyName);
        };

        vm.ResetToDefaults();

        Assert.Equal(123, settings.Current.Widget.X);
        Assert.Equal(456, settings.Current.Widget.Y);
        Assert.False(settings.Current.Widget.Autostart);
        Assert.True(settings.Current.Widget.HasShownFirstRunToast);
        Assert.Equal(dismissedUntil, settings.Current.Widget.SetupCardDismissedUntilUtc);
        Assert.Null(settings.Current.Alarms.WebhookCommand);
        Assert.True(settings.Current.Alarms.PaceEnabled);
        Assert.True(settings.Current.Alarms.RespectFocusAssist);
        Assert.Null(settings.Current.Alarms.ResetWavPath);
        Assert.Null(settings.Current.Alarms.ZeroStateWavPath);
        Assert.Equal(ThemeService.ThemeMocha, settings.Current.Display.Theme);
        Assert.Equal(5, settings.Current.RefreshMinutes);

        Assert.Contains(nameof(SettingsPanelViewModel.PaceEnabled), raised);
        Assert.Contains(nameof(SettingsPanelViewModel.RespectFocusAssist), raised);
        Assert.Contains(nameof(SettingsPanelViewModel.WebhookCommand), raised);
        Assert.Contains(nameof(SettingsPanelViewModel.Theme), raised);
        Assert.Contains(nameof(SettingsPanelViewModel.ResetWavPath), raised);
        Assert.Contains(nameof(SettingsPanelViewModel.ZeroStateWavPath), raised);
    }

    private static BucketViewModel BucketView(string provider, string label, DateTimeOffset resetAt)
    {
        var vm = new BucketViewModel();
        vm.Apply(provider, new Bucket
        {
            Id = label,
            Kind = "session",
            Label = label,
            PercentUsed = 10,
            ResetIso = resetAt,
        });
        return vm;
    }
}
