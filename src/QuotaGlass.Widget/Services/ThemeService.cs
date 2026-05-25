using System.Windows;
using Application = System.Windows.Application;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// NX-06 — swap the merged theme dictionary at runtime. "mocha" (default,
/// dark) and "latte" (light) are the two ResourceDictionaries shipped under
/// Theme/. Both define the SAME keys so consumers are theme-agnostic.
/// </summary>
public static class ThemeService
{
    public const string ThemeMocha = "mocha";
    public const string ThemeLatte = "latte";

    public static void Apply(string themeName)
    {
        var app = Application.Current;
        if (app is null) return;

        var sourceUri = themeName?.ToLowerInvariant() switch
        {
            ThemeLatte => new Uri("pack://application:,,,/Theme/CatppuccinLatte.xaml", UriKind.Absolute),
            _ => new Uri("pack://application:,,,/Theme/CatppuccinMocha.xaml", UriKind.Absolute),
        };

        // Find and replace the theme dictionary (always the first merged one
        // per App.xaml). Keep Controls.xaml in place.
        var merged = app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source is null) continue;
            var src = merged[i].Source.OriginalString;
            if (src.Contains("Catppuccin", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = new ResourceDictionary { Source = sourceUri };
                return;
            }
        }

        // Fallback: insert at index 0 if for some reason none was found.
        merged.Insert(0, new ResourceDictionary { Source = sourceUri });
    }
}
