using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using QuotaGlass.Widget.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace QuotaGlass.Widget.ViewModels;

/// <summary>
/// Bound to the embedded settings panel inside MainWindow. Mutates
/// <see cref="SettingsStore"/> directly so changes persist atomically and
/// other subscribers (AlarmScheduler, widget) react via the Changed event.
/// </summary>
public sealed class SettingsPanelViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// The full canonical ladder set rendered as checkboxes regardless of
    /// which subset is currently active in settings. Keep this in lockstep
    /// with <see cref="AlarmScheduler.DefaultLadder"/>.
    /// </summary>
    private static readonly int[] CanonicalLadderMinutes =
    {
        24 * 60, 12 * 60, 6 * 60, 3 * 60, 60, 30, 15, 5, 0,
    };

    private readonly SettingsStore _store;
    private bool _isExpanded;

    public ObservableCollection<LadderTierViewModel> LadderTiers { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise();
            Raise(nameof(ToggleGlyph));
            Raise(nameof(ToggleLabel));
        }
    }

    public string ToggleGlyph => _isExpanded ? "˄ Hide settings" : "˅ Show settings";

    public string ToggleLabel => _isExpanded ? "Hide settings" : "Settings";

    public bool AlarmsEnabled
    {
        get => _store.Current.Alarms.Enabled;
        set { _store.Update(s => s.Alarms.Enabled = value); Raise(); }
    }

    public bool PaceEnabled
    {
        get => _store.Current.Alarms.PaceEnabled;
        set { _store.Update(s => s.Alarms.PaceEnabled = value); Raise(); }
    }

    public bool RespectFocusAssist
    {
        get => _store.Current.Alarms.RespectFocusAssist;
        set { _store.Update(s => s.Alarms.RespectFocusAssist = value); Raise(); }
    }

    public string WebhookCommand
    {
        get => _store.Current.Alarms.WebhookCommand ?? string.Empty;
        set { _store.Update(s => s.Alarms.WebhookCommand = string.IsNullOrWhiteSpace(value) ? null : value); Raise(); }
    }

    /// <summary>NX-06 — `mocha` (dark) or `latte` (light).</summary>
    public string Theme
    {
        get => _store.Current.Display.Theme;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _store.Update(s => s.Display.Theme = value);
            ThemeService.Apply(value);
            Raise();
            Raise(nameof(IsLightTheme));
            Raise(nameof(IsDarkTheme));
        }
    }

    public bool IsLightTheme
    {
        get => string.Equals(Theme, ThemeService.ThemeLatte, StringComparison.OrdinalIgnoreCase);
        set { if (value) Theme = ThemeService.ThemeLatte; }
    }

    public bool IsDarkTheme
    {
        get => string.Equals(Theme, ThemeService.ThemeMocha, StringComparison.OrdinalIgnoreCase);
        set { if (value) Theme = ThemeService.ThemeMocha; }
    }

    public bool IsHighContrastTheme
    {
        get => string.Equals(Theme, ThemeService.ThemeHighContrast, StringComparison.OrdinalIgnoreCase);
        set { if (value) Theme = ThemeService.ThemeHighContrast; }
    }

    public bool IsSystemTheme
    {
        get => string.Equals(Theme, ThemeService.ThemeSystem, StringComparison.OrdinalIgnoreCase);
        set { if (value) Theme = ThemeService.ThemeSystem; }
    }

    public bool Autostart
    {
        get => _store.Current.Widget.Autostart;
        set
        {
            _store.Update(s => s.Widget.Autostart = value);
            AutostartRegistration.Apply(value);
            Raise();
        }
    }

    public int RefreshMinutes
    {
        get => _store.Current.RefreshMinutes;
        set { if (value < 1) return; _store.Update(s => s.RefreshMinutes = value); Raise(); }
    }

    public double WarnPercent
    {
        get => _store.Current.Display.WarnPercent;
        set { _store.Update(s => s.Display.WarnPercent = Math.Clamp(value, 1, 99)); Raise(); }
    }

    public double DangerPercent
    {
        get => _store.Current.Display.DangerPercent;
        set { _store.Update(s => s.Display.DangerPercent = Math.Clamp(value, 1, 99)); Raise(); }
    }

    public string LadderLabel
    {
        get
        {
            var ladder = _store.Current.Alarms.LadderMinutes;
            return ladder.Count == 0
                ? "(no alarms)"
                : string.Join(", ", ladder.Select(FormatMinutes));
        }
    }

    public string CustomWavPath
    {
        get => _store.Current.Alarms.CustomWavPath ?? "";
        set { _store.Update(s => s.Alarms.CustomWavPath = string.IsNullOrWhiteSpace(value) ? null : value); Raise(); }
    }

    public string CustomWavLabel => SoundLabel(CustomWavPath, "Default notification sound");

    public string ResetWavPath
    {
        get => _store.Current.Alarms.ResetWavPath ?? "";
        set { _store.Update(s => s.Alarms.ResetWavPath = string.IsNullOrWhiteSpace(value) ? null : value); Raise(); }
    }

    public string ResetWavLabel => SoundLabel(ResetWavPath, "Default reset sound");

    public string ZeroStateWavPath
    {
        get => _store.Current.Alarms.ZeroStateWavPath ?? "";
        set { _store.Update(s => s.Alarms.ZeroStateWavPath = string.IsNullOrWhiteSpace(value) ? null : value); Raise(); }
    }

    public string ZeroStateWavLabel => SoundLabel(ZeroStateWavPath, "Default zero-state sound");

    public SettingsPanelViewModel(SettingsStore store)
    {
        _store = store;
        RebuildLadderTiers();
        _store.Changed += (_, _) =>
        {
            // External writes (e.g. via JSON edit) should refresh the UI.
            RaiseAllSettingsProperties();
            RebuildLadderTiers();
        };
    }

    private void RebuildLadderTiers()
    {
        var enabledSet = new HashSet<int>(_store.Current.Alarms.LadderMinutes);
        LadderTiers.Clear();
        foreach (var minutes in CanonicalLadderMinutes)
        {
            LadderTiers.Add(new LadderTierViewModel(minutes, enabledSet.Contains(minutes), OnTierToggled));
        }
    }

    private void OnTierToggled(int minutes, bool enabled)
    {
        _store.Update(s =>
        {
            var list = new HashSet<int>(s.Alarms.LadderMinutes);
            if (enabled) list.Add(minutes);
            else list.Remove(minutes);
            // Persist in canonical biggest-first order for human-readable JSON.
            s.Alarms.LadderMinutes = CanonicalLadderMinutes
                .Where(m => list.Contains(m))
                .ToList();
        });
        Raise(nameof(LadderLabel));
    }

    public void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>R4-Q-11 — restore every setting to the value Settings.CreateDefault()
    /// emits. Keeps Widget.X/Y so the user's chosen position is preserved.</summary>
    public void ResetToDefaults()
    {
        var fresh = Settings.CreateDefault();
        // Preserve position so the widget doesn't jump back to (40,40).
        fresh.Widget.X = _store.Current.Widget.X;
        fresh.Widget.Y = _store.Current.Widget.Y;
        fresh.Widget.Autostart = _store.Current.Widget.Autostart;
        fresh.Widget.HasShownFirstRunToast = _store.Current.Widget.HasShownFirstRunToast;
        fresh.Widget.SetupCardDismissedUntilUtc = _store.Current.Widget.SetupCardDismissedUntilUtc;
        _store.Replace(fresh);
        ThemeService.Apply(fresh.Display.Theme);
    }

    public enum WavSlot { Custom, Reset, ZeroState }

    public string? PickWavFile(WavSlot slot = WavSlot.Custom)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Pick alarm sound ({slot})",
            Filter = "Audio (*.wav;*.mp3;*.m4a;*.aac;*.wma)|*.wav;*.mp3;*.m4a;*.aac;*.wma|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return null;
        switch (slot)
        {
            case WavSlot.Reset: ResetWavPath = dlg.FileName; break;
            case WavSlot.ZeroState: ZeroStateWavPath = dlg.FileName; break;
            default: CustomWavPath = dlg.FileName; break;
        }
        return dlg.FileName;
    }

    private static string FormatMinutes(int m) => m switch
    {
        0 => "at-reset",
        < 60 => $"{m}m",
        var x when x % 60 == 0 => $"{x / 60}h",
        _ => $"{m / 60}h{m % 60}m",
    };

    private void Raise([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    private void RaiseAllSettingsProperties()
    {
        Raise(nameof(AlarmsEnabled));
        Raise(nameof(PaceEnabled));
        Raise(nameof(RespectFocusAssist));
        Raise(nameof(WebhookCommand));
        Raise(nameof(Theme));
        Raise(nameof(IsLightTheme));
        Raise(nameof(IsDarkTheme));
        Raise(nameof(IsHighContrastTheme));
        Raise(nameof(IsSystemTheme));
        Raise(nameof(Autostart));
        Raise(nameof(RefreshMinutes));
        Raise(nameof(WarnPercent));
        Raise(nameof(DangerPercent));
        Raise(nameof(LadderLabel));
        Raise(nameof(CustomWavPath));
        Raise(nameof(CustomWavLabel));
        Raise(nameof(ResetWavPath));
        Raise(nameof(ResetWavLabel));
        Raise(nameof(ZeroStateWavPath));
        Raise(nameof(ZeroStateWavLabel));
    }

    private static string SoundLabel(string path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path)) return fallback;
        try
        {
            return Path.GetFileName(path);
        }
        catch
        {
            return path;
        }
    }
}

/// <summary>
/// One row of the per-tier alarm-ladder toggle list. Each tier is wired
/// back to the parent <see cref="SettingsPanelViewModel"/> via a callback
/// so the panel can rewrite <c>settings.json</c> atomically.
/// </summary>
public sealed class LadderTierViewModel : INotifyPropertyChanged
{
    private readonly Action<int, bool> _onToggle;
    private bool _isEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Minutes { get; }
    public string Label { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            _onToggle(Minutes, value);
        }
    }

    public LadderTierViewModel(int minutes, bool isEnabled, Action<int, bool> onToggle)
    {
        Minutes = minutes;
        _isEnabled = isEnabled;
        _onToggle = onToggle;
        Label = minutes switch
        {
            0 => "At reset",
            < 60 => $"{minutes} min",
            var x when x % 60 == 0 => $"{x / 60} h",
            _ => $"{minutes / 60} h {minutes % 60} m",
        };
    }
}
