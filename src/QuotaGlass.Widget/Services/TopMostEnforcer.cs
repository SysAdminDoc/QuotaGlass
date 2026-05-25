using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Keeps the widget window topmost using a WinEvent hook on a dedicated
/// background STA thread. Plain WPF <c>Topmost=true</c> is overridden by
/// UAC consent dialogs, fullscreen apps, and assorted system foreground
/// events; re-asserting <c>SetWindowPos(HWND_TOPMOST)</c> on every
/// foreground change papers over all of them.
///
/// Pattern adapted from Zrnik/claude-usage-windows-taskbar-widget under MIT.
/// Critical detail: the <see cref="WinEventDelegate"/> must be a field, not
/// a local — otherwise the GC collects it asynchronously in Release builds
/// and the hook starts crashing.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class TopMostEnforcer : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint WmQuit = 0x0012;
    private const uint WmUser = 0x0400;
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    private readonly IntPtr _hwnd;
    private volatile uint _threadId;
    private volatile bool _paused;
    private bool _disposed;

    // Must be a field. Capturing it in a local lets the GC collect the
    // delegate object asynchronously in Release builds and the hook fires
    // through a dangling pointer.
    private readonly WinEventDelegate _foregroundDelegate;

    public TopMostEnforcer(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _foregroundDelegate = OnForegroundChanged;
        var thread = new Thread(Run) { IsBackground = true, Name = "QuotaGlass.TopMost" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();

        var hook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground,
            IntPtr.Zero, _foregroundDelegate, 0, 0, WinEventOutOfContext);

        try
        {
            int ret;
            while ((ret = GetMessage(out var msg, IntPtr.Zero, 0, 0)) != 0)
            {
                if (ret == -1) break;
                if (msg.Message == WmUser)
                {
                    SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                    continue;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        }
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_paused) return;
        PostThreadMessage(_threadId, WmUser, IntPtr.Zero, IntPtr.Zero);
    }
}
