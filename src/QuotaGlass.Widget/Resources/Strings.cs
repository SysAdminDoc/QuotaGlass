using System.Globalization;
using System.Resources;

namespace QuotaGlass.Widget.Resources;

/// <summary>
/// R4-N9 — minimal localization scaffold. All UI strings should funnel
/// through <see cref="Get"/> so a future locale RESX (e.g.
/// <c>Strings.fr.resx</c>) drops in without touching code.
///
/// v0.7 ships only the English baseline keyed in <see cref="Defaults"/>;
/// the resource-manager lookup is wired so a `Strings.{culture}.resources`
/// file in the same satellite assembly path picks up at runtime via
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.
///
/// We use a static dictionary fallback rather than ResourceManager so that
/// the project compiles cleanly without a RESX/RESOURCE source-generator
/// dependency. When v0.8+ adds RESX files, swap the dictionary for a
/// <see cref="ResourceManager"/>; consumers (which only see Strings.Get)
/// don't need to change.
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        // Window chrome
        ["AppTitle"] = "QuotaGlass",
        ["TrayShow"] = "Show widget",
        ["TrayHide"] = "Hide widget",
        ["TrayRefresh"] = "Refresh now",
        ["TraySettings"] = "Settings…",
        ["TrayCheckUpdates"] = "Check for updates…",
        ["TrayResetPosition"] = "Reset widget position",
        ["TrayQuit"] = "Quit QuotaGlass",
        ["TrayWindowSub"] = "Window",
        ["TrayUpdatesSub"] = "Updates",
        ["FirstRunBalloonTitle"] = "QuotaGlass is in your tray",
        ["FirstRunBalloonBody"] = "Click the tray icon to show / hide the widget. Right-click for more.",

        // Setup card
        ["SetupHeader"] = "Finish setup",
        ["SetupInstallExtension"] = "Install extension",
        ["SetupRunRegister"] = "Register host",
        ["SetupHelp"] = "Troubleshooting",
        ["SetupDismiss24h"] = "Later",
        ["SetupDismiss24hToolTip"] = "Hide the setup card for 24 hours.",

        // Settings panel
        ["SettingsHide"] = "˄ Hide settings",
        ["SettingsShow"] = "˅ Show settings",
        ["SettingsAlarmsEnabled"] = "Notification alarms enabled",
        ["SettingsAutostart"] = "Launch QuotaGlass at logon",
        ["SettingsPaceEnabled"] = "Fire pace (burn-rate) warning toasts",
        ["SettingsFocusAssist"] = "Respect Windows Focus Assist / DND",
        ["SettingsTheme"] = "Theme:",
        ["SettingsThemeMocha"] = "Mocha (dark)",
        ["SettingsThemeLatte"] = "Latte (light)",
        ["SettingsThemeHighContrast"] = "High contrast",
        ["SettingsThemeSystem"] = "Follow system",
        ["SettingsResetDefaults"] = "Reset settings to defaults",
        ["SettingsWarnLabel"] = "Warn at %",
        ["SettingsDangerLabel"] = "Danger at %",
        ["SettingsCustomSound"] = "Custom sound:",
        ["SettingsResetSound"] = "Reset (R2) sound:",
        ["SettingsZeroStateSound"] = "Zero-state (R3) sound:",
        ["SettingsPick"] = "Pick…",
        ["SettingsWebhookLabel"] = "Webhook command on alarm fire (optional):",
        ["SettingsLadderHeader"] = "Reset ladder (uncheck to suppress a tier):",

        // Calendar
        ["CalendarShow"] = "˅ Show 7-day reset calendar",
        ["CalendarHide"] = "˄ Hide 7-day reset calendar",

        // Log panel
        ["LogShow"] = "˅ Show log",
        ["LogHide"] = "˄ Hide log",
        ["LogEmpty"] = "(no log entries today)",
        ["LogNotFound"] = "(no logs yet)",

        // Snooze
        ["Snooze1h"] = "Snooze 1 hour",
        ["Snooze6h"] = "Snooze 6 hours",
        ["Snooze24h"] = "Snooze 24 hours",
        ["SnoozeUntilReset"] = "Snooze until reset",
        ["Unsnooze"] = "Unsnooze",

        // Status text
        ["StatusStarting"] = "Starting up…",
        ["StatusWaitingForFirstSnapshot"] = "Waiting for first snapshot from extension…",
    };

    /// <summary>
    /// Resource manager lookup that prefers a localized satellite assembly
    /// matching the current UI culture, falling back to the English
    /// <see cref="Defaults"/> dictionary baked into the executable.
    /// </summary>
    public static string Get(string key)
    {
        // Hook for future RESX-backed localization. Today only the
        // default dictionary is consulted.
        if (Defaults.TryGetValue(key, out var value)) return value;
        return key; // surface unknown keys verbatim so missing strings are visible.
    }

    public static string SetupHeader => Get(nameof(SetupHeader));
    public static string SetupInstallExtension => Get(nameof(SetupInstallExtension));
    public static string SetupRunRegister => Get(nameof(SetupRunRegister));
    public static string SetupHelp => Get(nameof(SetupHelp));
    public static string SetupDismiss24h => Get(nameof(SetupDismiss24h));
    public static string SetupDismiss24hToolTip => Get(nameof(SetupDismiss24hToolTip));

    /// <summary>R4-N9 — set the UI culture at startup. Future v0.8 wires a
    /// settings entry; v0.7 just exposes the API so localization-aware
    /// callers can prepare for it.</summary>
    public static void SetUiCulture(string? cultureName)
    {
        try
        {
            var culture = string.IsNullOrEmpty(cultureName)
                ? CultureInfo.InstalledUICulture
                : CultureInfo.GetCultureInfo(cultureName!);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Ignore — unknown locale falls back to invariant English.
        }
    }
}
