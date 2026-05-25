using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuotaGlass.Widget.ViewModels;

/// <summary>
/// L-02 — collapsible 7-day "next resets" view inside the settings panel.
/// Aggregates every bucket's <c>ResetIso</c> into per-day groups so users
/// can plan around future renewals.
/// </summary>
public sealed class CalendarViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CalendarDayViewModel> Days { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; Raise(); Raise(nameof(ToggleLabel)); }
    }

    public string ToggleLabel => _isExpanded ? "Hide reset calendar" : "Reset calendar";

    public void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>
    /// Rebuild from the current bucket viewmodels. Cheap; collection is at
    /// most 7 days × small bucket count.
    /// </summary>
    public void Rebuild(IEnumerable<BucketViewModel> buckets)
    {
        var todayLocal = DateTimeOffset.Now.Date;
        var horizon = todayLocal.AddDays(7);

        // Day buckets keyed by local Date.
        var days = new Dictionary<DateTime, CalendarDayViewModel>();
        for (var d = 0; d < 7; d++)
        {
            var date = todayLocal.AddDays(d);
            days[date] = new CalendarDayViewModel(date);
        }

        foreach (var b in buckets)
        {
            if (b is null) continue;
            var resetLocal = b.NextResetLocal;
            if (resetLocal is null) continue;
            var date = resetLocal.Value.Date;
            if (date < todayLocal || date >= horizon) continue;
            if (!days.TryGetValue(date, out var day)) continue;
            day.Resets.Add(new CalendarEntry(b.Provider, b.Label, resetLocal.Value, b.Percent));
        }

        Days.Clear();
        foreach (var day in days.Values.OrderBy(d => d.Date))
        {
            if (day.Resets.Count == 0) continue;
            var ordered = day.Resets.OrderBy(r => r.ResetLocal).ToList();
            day.Resets.Clear();
            foreach (var entry in ordered)
            {
                day.Resets.Add(entry);
            }
            Days.Add(day);
        }
        Raise(nameof(Days));
        Raise(nameof(IsEmpty));
    }

    public bool IsEmpty => Days.Count == 0;

    private void Raise([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public sealed class CalendarDayViewModel
{
    public DateTime Date { get; }
    public ObservableCollection<CalendarEntry> Resets { get; } = new();

    public string Header
    {
        get
        {
            var today = DateTime.Today;
            if (Date == today) return $"Today — {Date:dddd MMM d}";
            if (Date == today.AddDays(1)) return $"Tomorrow — {Date:dddd MMM d}";
            return Date.ToString("dddd, MMM d");
        }
    }

    public CalendarDayViewModel(DateTime date) => Date = date;
}

public sealed record CalendarEntry(string Provider, string Label, DateTimeOffset ResetLocal, double Percent)
{
    public string Display => $"{ResetLocal:t} · {Provider} {Label} ({Percent:0}%)";
}
