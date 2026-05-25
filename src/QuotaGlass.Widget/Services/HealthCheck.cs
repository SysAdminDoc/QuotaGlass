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
        var snapshotPath = LatestSnapshotPath();
        var snapshotExists = snapshotPath is not null;

        DateTimeOffset? snapshotTs = null;
        if (snapshotPath is not null)
        {
            try
            {
                snapshotTs = File.GetLastWriteTimeUtc(snapshotPath);
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

    private static string? LatestSnapshotPath()
    {
        var candidates = new List<(string Path, DateTime LastWriteUtc)>();
        foreach (var path in new[] { AppPaths.SnapshotFile, AppPaths.LocalCredsSnapshotFile })
        {
            try
            {
                if (File.Exists(path))
                {
                    candidates.Add((path, File.GetLastWriteTimeUtc(path)));
                }
            }
            catch
            {
                // Snapshot files can be swapped atomically while probing.
            }
        }

        candidates.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
        return candidates.Count == 0 ? null : candidates[0].Path;
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
