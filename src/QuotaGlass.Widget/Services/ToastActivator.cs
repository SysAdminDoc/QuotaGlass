using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using QuotaGlass.Widget.ViewModels;
using Application = System.Windows.Application;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// L-04 / R4-N2 — hand-rolled COM activator for toast actions (Snooze 1h /
/// Open analytics). Implements <see cref="INotificationActivationCallback"/>
/// so Windows Action Center routes button clicks back to the running widget
/// process (or relaunches it) with the action's argument string.
///
/// We intentionally do NOT depend on <c>Microsoft.Toolkit.Uwp.Notifications</c>
/// — Pass 2 R2-P0-01 removed it for the GHSA-rxg9-xrhp-64gj CVE; re-adding
/// would re-introduce the vulnerable transitive <c>System.Drawing.Common
/// 4.7.0</c>. Hand-rolling the COM glue keeps the dependency cost at zero
/// and the source fully auditable.
///
/// CLSID stays stable across releases — the Start Menu shortcut's
/// <c>System.AppUserModel.ToastActivatorCLSID</c> property maps the toast's
/// activation back to this class via HKCU\Software\Classes\CLSID\{guid}.
/// Installer registers + unregisters the key.
/// </summary>
[ComVisible(true)]
[Guid(Clsid)]
[ClassInterface(ClassInterfaceType.None)]
[ComSourceInterfaces(typeof(INotificationActivationCallback))]
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class ToastActivator : INotificationActivationCallback
{
    /// <summary>Stable activator CLSID. Pinned for the life of the project.</summary>
    public const string Clsid = "4F1B3F6E-2D8C-4E83-9C12-9B0B17F8D2A2";

    public void Activate(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] NotificationUserInputData[] data,
        uint dataCount)
    {
        // Activation runs on a COM thread — marshal back to the dispatcher
        // so any UI work happens on the WPF UI thread.
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            dispatcher.BeginInvoke(() => HandleAction(invokedArgs));
        }
        catch (Exception ex)
        {
            WidgetLogger.Error("Toast activation failed", ex);
        }
    }

    private static void HandleAction(string invokedArgs)
    {
        if (string.IsNullOrEmpty(invokedArgs)) return;

        // Argument shape produced by ToastService.Show:
        //   action=snooze;bucket=<id>;duration=PT1H
        //   action=open;url=https://...
        var parts = invokedArgs.Split(';');
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
            var idx = p.IndexOf('=');
            if (idx > 0) map[p[..idx]] = p[(idx + 1)..];
        }

        if (!map.TryGetValue("action", out var action)) return;

        if (string.Equals(action, "snooze", StringComparison.OrdinalIgnoreCase)
            && map.TryGetValue("bucket", out var bucketId)
            && Application.Current?.MainWindow?.DataContext is MainViewModel vm)
        {
            var duration = ParseIsoDuration(map.GetValueOrDefault("duration", "PT1H"));
            vm.SnoozeBucket(bucketId, duration);
            WidgetLogger.Info($"Toast snoozed bucket {bucketId} for {duration}");
            return;
        }

        if (string.Equals(action, "open", StringComparison.OrdinalIgnoreCase)
            && map.TryGetValue("url", out var url))
        {
            OpenUrlSafe(url);
        }
    }

    private static TimeSpan ParseIsoDuration(string iso) => iso switch
    {
        "PT1H" => TimeSpan.FromHours(1),
        "PT6H" => TimeSpan.FromHours(6),
        "PT24H" or "P1D" => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(1),
    };

    private static void OpenUrlSafe(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }
}

/// <summary>
/// Native Windows interface — matches notificationactivationcallback.idl.
/// Implementing this on a COM-visible class is what lets Action Center
/// invoke our widget when the user clicks a toast action button.
/// </summary>
[ComImport]
[Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback
{
    void Activate(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] NotificationUserInputData[] data,
        uint dataCount);
}

[StructLayout(LayoutKind.Sequential)]
public struct NotificationUserInputData
{
    [MarshalAs(UnmanagedType.LPWStr)] public string Key;
    [MarshalAs(UnmanagedType.LPWStr)] public string Value;
}
