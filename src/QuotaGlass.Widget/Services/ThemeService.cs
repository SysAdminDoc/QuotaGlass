using System.Windows;
using Application = System.Windows.Application;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// NX-06 / R4-N8 — swap the merged theme dictionary at runtime. Themes are
/// stored under Theme/ as `CatppuccinMocha.xaml` (dark default),
/// `CatppuccinLatte.xaml` (light), and `HighContrast.xaml` (Windows HC
/// schemes; binds to <c>SystemColors</c> dynamic resources). Every theme
/// dictionary defines the SAME keys so consumers are theme-agnostic.
///
/// "system" mode picks Mocha / Latte / HighContrast at <see cref="Apply"/>
/// time by reading <see cref="System.Windows.SystemParameters.HighContrast"/>
/// and the apps-use-light-theme registry preference.
/// </summary>
public static class ThemeService
{
    public const string ThemeMocha = "mocha";
    public const string ThemeLatte = "latte";
    public const string ThemeHighContrast = "highcontrast";
    public const string ThemeSystem = "system";

    public static void Apply(string themeName)
    {
        var app = Application.Current;
        if (app is null) return;

        var resolved = ResolveTheme(themeName);
        var sourceUri = resolved switch
        {
            ThemeLatte => new Uri("pack://application:,,,/Theme/CatppuccinLatte.xaml", UriKind.Absolute),
            ThemeHighContrast => new Uri("pack://application:,,,/Theme/HighContrast.xaml", UriKind.Absolute),
            _ => new Uri("pack://application:,,,/Theme/CatppuccinMocha.xaml", UriKind.Absolute),
        };

        // Find and replace the theme dictionary (always the first merged one
        // per App.xaml). Keep Controls.xaml in place.
        var merged = app.Resources.MergedDictionaries;
        var swapped = false;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source is null) continue;
            var src = merged[i].Source.OriginalString;
            if (src.Contains("Catppuccin", StringComparison.OrdinalIgnoreCase)
                || src.Contains("HighContrast", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = new ResourceDictionary { Source = sourceUri };
                swapped = true;
                break;
            }
        }

        // Fallback: insert at index 0 if for some reason none was found.
        if (!swapped) merged.Insert(0, new ResourceDictionary { Source = sourceUri });

        // R4-P0-03 — re-apply the Mica brush override if it was previously
        // active. The dictionary swap above resets `Brush.Window.Background`
        // to the new theme's full-alpha brush; without this, the Mica
        // backdrop would silently regress to occluded.
        if (MicaBackdrop.WasApplied)
        {
            MicaBackdrop.ApplyMicaBrushOverride();
        }
    }

    /// <summary>
    /// R4-N8 — translate "system" to one of the three concrete themes by
    /// reading Windows accessibility + light/dark preferences. Concrete
    /// theme names pass through unchanged.
    /// </summary>
    private static string ResolveTheme(string? themeName)
    {
        if (string.Equals(themeName, ThemeSystem, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (SystemParameters.HighContrast) return ThemeHighContrast;
            }
            catch { /* SystemParameters can throw on first access during shutdown */ }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsLight = key?.GetValue("AppsUseLightTheme");
                if (appsLight is int v && v != 0) return ThemeLatte;
            }
            catch { /* registry probe failures fall through to dark */ }

            return ThemeMocha;
        }

        return themeName?.ToLowerInvariant() ?? ThemeMocha;
    }
}
