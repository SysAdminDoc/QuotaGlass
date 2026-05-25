using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Applies Windows 11 Mica/Acrylic system backdrop via DwmSetWindowAttribute.
/// No-op on Windows 10 / older Win11. Documentation:
/// https://learn.microsoft.com/en-us/windows/apps/design/style/mica
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class MicaBackdrop
{
    private enum BackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4,
    }

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// Enable Mica on Win11 22621+. Sets dark-mode title-bar + Mica
    /// backdrop. Caller should pair with a transparent window background.
    /// Returns true if applied successfully.
    /// </summary>
    public static bool TryApply(Window window, bool acrylic = false)
    {
        if (!IsSupported()) return false;

        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            // Dark immersive title bar.
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var backdrop = (int)(acrylic ? BackdropType.Acrylic : BackdropType.Mica);
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

            // Backdrop requires a transparent window background AND a thin
            // chrome-border alpha. Otherwise the Mocha.Base @ 0.92 brush in
            // WindowChromeBorder occludes the Mica composition.
            // R3-P0-03: swap the Brush.Window.Background app-resource to the
            // thin-alpha variant so every consumer (chrome border included)
            // becomes transparent to Mica.
            window.Background = Brushes.Transparent;
            try
            {
                var resources = Application.Current?.Resources;
                if (resources is not null && resources.Contains("Brush.Window.MicaBackground"))
                {
                    resources["Brush.Window.Background"] = resources["Brush.Window.MicaBackground"];
                }
            }
            catch
            {
                // Resource swap is purely cosmetic — never fail TryApply on this.
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSupported()
    {
        // Mica + system backdrop API requires Win11 22621 (22H2).
        try
        {
            var v = Environment.OSVersion.Version;
            return v.Major >= 10 && (v.Build >= 22621);
        }
        catch
        {
            return false;
        }
    }
}
