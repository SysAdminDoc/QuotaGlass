using System.Windows;
using System.Windows.Controls;
using QuotaGlass.Widget.ViewModels;

namespace QuotaGlass.Widget.Services;

internal static class BucketContextMenuService
{
    public static void Show(UIElement target, BucketViewModel bucket, MainViewModel root)
    {
        var bucketId = bucket.Id;
        if (string.IsNullOrEmpty(bucketId)) return;

        var menu = new ContextMenu();
        void Add(string header, TimeSpan duration)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => root.SnoozeBucket(bucketId, duration);
            menu.Items.Add(item);
        }

        Add("Snooze 1 hour", TimeSpan.FromHours(1));
        Add("Snooze 6 hours", TimeSpan.FromHours(6));
        Add("Snooze 24 hours", TimeSpan.FromHours(24));

        var untilReset = bucket.NextResetLocal.HasValue
            ? bucket.NextResetLocal.Value.ToUniversalTime() - DateTimeOffset.UtcNow
            : TimeSpan.FromDays(8);
        if (untilReset < TimeSpan.FromMinutes(5)) untilReset = TimeSpan.FromMinutes(5);
        Add("Snooze until reset", untilReset);

        if (root.SettingsStore.Current.Alarms.SnoozedBucketsUntilUtc.ContainsKey(bucketId))
        {
            menu.Items.Add(new Separator());
            var unsnooze = new MenuItem { Header = "Unsnooze" };
            unsnooze.Click += (_, _) => root.UnsnoozeBucket(bucketId);
            menu.Items.Add(unsnooze);
        }

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }
}
