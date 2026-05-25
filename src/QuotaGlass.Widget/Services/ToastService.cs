using System.IO;
using System.Media;
using System.Runtime.Versioning;
using System.Windows.Media;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace QuotaGlass.Widget.Services;

public interface IToastService
{
    void Show(string title, string? body = null, string? customWavPath = null, string? tag = null, IReadOnlyList<ToastAction>? actions = null);
}

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
public sealed class ToastService : IToastService
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
    /// <param name="actions">L-04 / R4-N2 — optional list of action buttons. Click routes through ToastActivator.</param>
    public void Show(string title, string? body = null, string? customWavPath = null, string? tag = null, IReadOnlyList<ToastAction>? actions = null)
    {
        var xml = new XmlDocument();
        var bodyLine = string.IsNullOrEmpty(body) ? string.Empty : $"\n          <text>{Escape(body)}</text>";
        var hasCustomAudio = !string.IsNullOrEmpty(customWavPath) && File.Exists(customWavPath);
        var audioFragment = hasCustomAudio
            ? "\n  <audio silent=\"true\"/>"
            : string.Empty;
        var actionsFragment = BuildActionsXml(actions);

        xml.LoadXml($"""
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>{Escape(title)}</text>{bodyLine}
                </binding>
              </visual>{audioFragment}{actionsFragment}
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

    // WPF MediaPlayer instances must be kept alive for the duration of
    // playback; rooting them in this static list prevents GC mid-clip.
    private static readonly List<MediaPlayer> _activePlayers = new();
    private static readonly object _playerGate = new();

    private static void PlayWav(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            // WAV: keep SoundPlayer (no Media Foundation dep, lowest-latency).
            if (string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
            {
                new SoundPlayer(path).Play();
                return;
            }

            // MP3/M4A/anything-else: route through WPF MediaPlayer which
            // calls Media Foundation. No NAudio dependency. Toast UI thread
            // is the natural owner of MediaPlayer instances.
            PlayMediaFoundation(path);
        }
        catch
        {
            // Audio playback failure must never break the visible toast.
        }
    }

    private static void PlayMediaFoundation(string path)
    {
        // MediaPlayer is thread-affine to a dispatcher; create it on the UI
        // thread when available, otherwise spin up a transient one.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // Headless / no-app context (e.g. test harness) — no audio.
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                var player = new MediaPlayer();
                lock (_playerGate) _activePlayers.Add(player);
                player.MediaEnded += (_, _) => Release(player);
                player.MediaFailed += (_, _) => Release(player);
                player.Open(new Uri(path, UriKind.Absolute));
                player.Play();
            }
            catch
            {
                // ignored
            }
        });
    }

    private static void Release(MediaPlayer player)
    {
        try
        {
            player.Stop();
            player.Close();
        }
        catch { }
        lock (_playerGate) _activePlayers.Remove(player);
    }

    /// <summary>Builds the <c>&lt;actions&gt;</c> fragment if any actions are
    /// supplied. Empty string when actions is null/empty — keeps the toast XML
    /// backward-compatible with v0.5 callers.</summary>
    private static string BuildActionsXml(IReadOnlyList<ToastAction>? actions)
    {
        if (actions is null || actions.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.Append("\n  <actions>");
        foreach (var a in actions)
        {
            sb.Append("\n    <action ");
            sb.Append($"content=\"{Escape(a.Content)}\" ");
            sb.Append($"arguments=\"{Escape(a.Arguments)}\" ");
            sb.Append("activationType=\"foreground\"/>");
        }
        sb.Append("\n  </actions>");
        return sb.ToString();
    }

    /// <summary>R4-N7 — delegates to <see cref="QuotaGlass.Shared.XmlEscape"/>
    /// so the escape logic is unit-testable from the test project (which
    /// doesn't reference Widget).</summary>
    private static string Escape(string s) => QuotaGlass.Shared.XmlEscape.Escape(s);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>
/// L-04 / R4-N2 — one button on a toast. <see cref="Content"/> is the
/// visible label; <see cref="Arguments"/> is passed to
/// <see cref="ToastActivator.Activate"/> when the user clicks it.
/// Convention: <c>action=&lt;name&gt;;bucket=&lt;id&gt;;duration=&lt;iso&gt;</c>.
/// </summary>
public sealed record ToastAction(string Content, string Arguments);
