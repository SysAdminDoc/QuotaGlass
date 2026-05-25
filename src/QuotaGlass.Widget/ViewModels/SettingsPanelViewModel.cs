using System.ComponentModel;
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
    private readonly SettingsStore _store;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; Raise(); Raise(nameof(ToggleGlyph)); }
    }

    public string ToggleGlyph => _isExpanded ? "˄ Hide settings" : "˅ Show settings";

    public bool AlarmsEnabled
    {
        get => _store.Current.Alarms.Enabled;
        set { _store.Update(s => s.Alarms.Enabled = value); Raise(); }
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

    public SettingsPanelViewModel(SettingsStore store)
    {
        _store = store;
        _store.Changed += (_, _) =>
        {
            // External writes (e.g. via JSON edit) should refresh the UI.
            Raise(nameof(AlarmsEnabled));
            Raise(nameof(Autostart));
            Raise(nameof(RefreshMinutes));
            Raise(nameof(WarnPercent));
            Raise(nameof(DangerPercent));
            Raise(nameof(LadderLabel));
            Raise(nameof(CustomWavPath));
        };
    }

    public void Toggle() => IsExpanded = !IsExpanded;

    public string? PickWavFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick a custom alarm sound",
            Filter = "Audio (*.wav)|*.wav|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return null;
        CustomWavPath = dlg.FileName;
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
}
