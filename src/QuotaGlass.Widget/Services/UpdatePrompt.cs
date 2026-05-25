using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace QuotaGlass.Widget.Services;

internal static class UpdatePrompt
{
    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateChecker.CheckAsync().ConfigureAwait(true);
            if (update is null)
            {
                MessageBox.Show($"QuotaGlass v{UpdateChecker.CurrentVersion} is up to date.",
                    "QuotaGlass", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var message = $"A new version is available: v{update.LatestVersion}\n\n"
                + $"Current version: v{UpdateChecker.CurrentVersion}\n"
                + $"Download: {update.AssetName}\n\n"
                + "Install now? The app will restart automatically.";
            var result = MessageBox.Show(message, "QuotaGlass update",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.OK)
            {
                UpdateChecker.LaunchSelfReplace(update);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update check failed: {ex.Message}", "QuotaGlass",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
