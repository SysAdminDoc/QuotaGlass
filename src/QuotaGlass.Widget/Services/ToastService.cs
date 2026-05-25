using System.IO;
using System.Media;
using System.Runtime.Versioning;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Hand-rolled toast notifier on raw <see cref="ToastNotificationManager"/>.
/// No <c>Microsoft.Toolkit.Uwp.Notifications</c> dependency — that package
/// pulls in vulnerable <c>System.Drawing.Common 4.7.0</c> and adds no
/// capability we need.
///
/// Per the authoritative UWP toast schema, <c>&lt;audio src=&quot;file:///...&quot;&gt;</c>
/// is silently ignored for unpackaged apps. We use <c>&lt;audio silent="true"/&gt;</c>
/// and play the user's chosen sound via <see cref="SoundPlayer"/> directly,
/// in lockstep with the toast.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class ToastService
{
    /// <summary>
    /// AppUserModelID for toast grouping / Action Center identity.
    /// Must match the Start Menu shortcut's AUMID set by the installer.
    /// </summary>
    public const string AppUserModelId = "com.sysadmindoc.QuotaGlass.Widget";

    private readonly ToastNotifier _notifier;

    public ToastService(string? aumid = null)
    {
        _notifier = ToastNotificationManager.CreateToastNotifier(aumid ?? AppUserModelId);
    }

    /// <param name="title">First text line. Required.</param>
    /// <param name="body">Second text line. Optional.</param>
    /// <param name="customWavPath">Absolute path to a WAV. If non-null, played alongside the silent toast.</param>
    /// <param name="tag">Stable tag — used for de-dup in Action Center. Same tag replaces in place.</param>
    public void Show(string title, string? body = null, string? customWavPath = null, string? tag = null)
    {
        var xml = new XmlDocument();
        var bodyLine = string.IsNullOrEmpty(body) ? string.Empty : $"\n          <text>{Escape(body)}</text>";
        var hasCustomAudio = !string.IsNullOrEmpty(customWavPath) && File.Exists(customWavPath);
        var audioFragment = hasCustomAudio
            ? "\n  <audio silent=\"true\"/>"
            : string.Empty;

        xml.LoadXml($"""
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>{Escape(title)}</text>{bodyLine}
                </binding>
              </visual>{audioFragment}
            </toast>
            """);

        var notification = new ToastNotification(xml);
        if (!string.IsNullOrEmpty(tag))
        {
            // Tag (and an empty group) makes the toast replace itself rather
            // than stacking 8 alarm-ladder entries in Action Center.
            notification.Tag = Truncate(tag, 64);
            notification.Group = "QuotaGlass";
        }

        _notifier.Show(notification);

        if (hasCustomAudio)
        {
            PlayWav(customWavPath!);
        }
    }

    private static void PlayWav(string path)
    {
        try
        {
            // Background-load + async-play; toast fires immediately, sound
            // catches up. SoundPlayer.Play is non-blocking and safe to call
            // from any thread.
            var player = new SoundPlayer(path);
            player.Play();
        }
        catch
        {
            // Audio playback failure must never break the visible toast.
        }
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
