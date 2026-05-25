using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// R3-P2-04 — wraps <c>SHQueryUserNotificationState</c> so the alarm
/// scheduler can suppress non-priority toasts when the user is in
/// Do-Not-Disturb / Focus Assist / a fullscreen app / presentation mode.
/// Documented at
/// https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate
/// </summary>
[SupportedOSPlatform("windows")]
public static class FocusAssist
{
    public enum UserNotificationState
    {
        Unknown = 0,
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        AppRunningD3DFullScreen = 7,
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHQueryUserNotificationState(out UserNotificationState pquns);

    public static UserNotificationState QueryState()
    {
        try
        {
            var hr = SHQueryUserNotificationState(out var state);
            return hr == 0 ? state : UserNotificationState.Unknown;
        }
        catch
        {
            return UserNotificationState.Unknown;
        }
    }

    /// <summary>
    /// True when alarms should be suppressed — any "DND-equivalent" state.
    /// <see cref="UserNotificationState.AcceptsNotifications"/> /
    /// <see cref="UserNotificationState.Unknown"/> / <see cref="UserNotificationState.NotPresent"/>
    /// pass through.
    /// </summary>
    public static bool ShouldSuppressToasts()
    {
        return QueryState() switch
        {
            UserNotificationState.Busy => true,
            UserNotificationState.RunningD3DFullScreen => true,
            UserNotificationState.AppRunningD3DFullScreen => true,
            UserNotificationState.PresentationMode => true,
            UserNotificationState.QuietTime => true,
            _ => false,
        };
    }
}
