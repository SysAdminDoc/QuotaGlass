using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Probes for the three preconditions that gate end-to-end QuotaGlass:
///   1. Browser extension installed and our nativeMessaging permission granted.
///   2. NMH registered in HKCU under at least one supported browser.
///   3. A snapshot has been received recently.
///
/// The Setup Checklist (F-N3) renders one row per step with a "fix"
/// affordance — install link, run-register button, troubleshooting link.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HealthCheck
{
    private const string HostName = "com.sysadmindoc.quotaglass";

    private static readonly string[] ChromiumKeys =
    {
        @"Software\Google\Chrome\NativeMessagingHosts\" + HostName,
        @"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName,
        @"Software\Chromium\NativeMessagingHosts\" + HostName,
    };

    private const string FirefoxKey = @"Software\Mozilla\NativeMessagingHosts\" + HostName;

    public HealthSnapshot Probe()
    {
        var nmhRegistered = ChromiumKeys.Any(KeyExists) || KeyExists(FirefoxKey);
        var snapshotExists = File.Exists(AppPaths.SnapshotFile);

        DateTimeOffset? snapshotTs = null;
        if (snapshotExists)
        {
            try
            {
                snapshotTs = File.GetLastWriteTimeUtc(AppPaths.SnapshotFile);
            }
            catch { }
        }

        // We can't directly probe the extension's install state from outside
        // the browser without filesystem access to per-browser profiles. We
        // infer it from "did we ever receive a snapshot?" — if yes, the
        // extension is alive AND the bridge is working.
        var extensionLikelyInstalled = snapshotExists && snapshotTs.HasValue
            && (DateTimeOffset.UtcNow - snapshotTs.Value) < TimeSpan.FromHours(24);

        return new HealthSnapshot(
            ExtensionLikelyInstalled: extensionLikelyInstalled,
            NmhRegistered: nmhRegistered,
            FirstSnapshotReceived: snapshotExists,
            LastSnapshotAt: snapshotTs);
    }

    private static bool KeyExists(string subKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record HealthSnapshot(
    bool ExtensionLikelyInstalled,
    bool NmhRegistered,
    bool FirstSnapshotReceived,
    DateTimeOffset? LastSnapshotAt)
{
    public bool AllGood => ExtensionLikelyInstalled && NmhRegistered && FirstSnapshotReceived;
}
