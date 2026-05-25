using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// System tray presence. Right-click menu (Show / Hide / Refresh / Quit),
/// double-click toggles the widget. Generates its own tray icon at runtime
/// so the project doesn't need to ship .ico assets yet (badge color is the
/// worst-bucket utilization once <see cref="UpdateBadge"/> is called).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconService : IDisposable
{
    // Win32 user-object handle freed in lockstep with each Icon swap so the
    // tray doesn't bleed GDI handles. Icon.FromHandle does NOT take ownership
    // of its HICON — Icon.Dispose leaves the underlying handle leaked.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public event EventHandler? ShowRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly Forms.ToolStripMenuItem _hideItem;
    private bool _disposed;
    private double _badgePercent = 0;
    private IntPtr _currentHIcon = IntPtr.Zero;

    public TrayIconService()
    {
        _showItem = new Forms.ToolStripMenuItem("Show widget", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        _hideItem = new Forms.ToolStripMenuItem("Hide widget", null, (_, _) => HideRequested?.Invoke(this, EventArgs.Empty));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(_hideItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Refresh now", null,
            (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null,
            (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit QuotaGlass", null,
            (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty)));

        var (initialIcon, initialHandle) = RenderIcon(_badgePercent);
        _currentHIcon = initialHandle;
        _icon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "QuotaGlass",
            Icon = initialIcon,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += OnDoubleClick;
    }

    /// <summary>Worst-bucket percent — 0..100. Repaints the tray badge.</summary>
    public void UpdateBadge(double worstPercent)
    {
        if (Math.Abs(_badgePercent - worstPercent) < 0.5) return;
        _badgePercent = worstPercent;

        var previousIcon = _icon.Icon;
        var previousHandle = _currentHIcon;

        var (nextIcon, nextHandle) = RenderIcon(_badgePercent);
        _currentHIcon = nextHandle;
        _icon.Icon = nextIcon;
        _icon.Text = $"QuotaGlass — worst bucket {worstPercent:0}%";

        // Dispose the managed wrapper, then release the Win32 HICON the wrapper
        // didn't own. NotifyIcon retains its own reference until we reassign.
        previousIcon?.Dispose();
        if (previousHandle != IntPtr.Zero) DestroyIcon(previousHandle);
    }

    public void NotifyFirstRun()
    {
        _icon.BalloonTipTitle = "QuotaGlass is in your tray";
        _icon.BalloonTipText = "Click the tray icon to show / hide the widget. Right-click for more.";
        _icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        try { _icon.ShowBalloonTip(4000); } catch { }
    }

    public void OnVisibilityChanged(bool widgetVisible)
    {
        _showItem.Enabled = !widgetVisible;
        _hideItem.Enabled = widgetVisible;
    }

    private void OnDoubleClick(object? sender, EventArgs e)
    {
        // Toggle.
        if (Application.Current.MainWindow is { IsVisible: true })
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static (Icon Icon, IntPtr Handle) RenderIcon(double percent)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Glass capsule background using Catppuccin Mantle.
            using var bg = new SolidBrush(Color.FromArgb(220, 24, 24, 37));
            g.FillEllipse(bg, 1, 1, size - 2, size - 2);

            // Ring track.
            using var trackPen = new Pen(Color.FromArgb(255, 69, 71, 90), 4);
            g.DrawArc(trackPen, 4, 4, size - 8, size - 8, 0, 360);

            // Sweep — color tracks ramp (matches RadialRing thresholds).
            var sweep = (float)(360.0 * Math.Clamp(percent, 0, 100) / 100.0);
            if (sweep > 0)
            {
                var color = percent switch
                {
                    >= 85 => Color.FromArgb(255, 243, 139, 168),  // red / Mocha.Red
                    >= 60 => Color.FromArgb(255, 250, 179, 135),  // peach
                    _ => Color.FromArgb(255, 166, 227, 161),       // green
                };
                using var sweepPen = new Pen(color, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(sweepPen, 4, 4, size - 8, size - 8, -90, sweep);
            }
        }
        var handle = bmp.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        if (_currentHIcon != IntPtr.Zero)
        {
            DestroyIcon(_currentHIcon);
            _currentHIcon = IntPtr.Zero;
        }
    }
}
