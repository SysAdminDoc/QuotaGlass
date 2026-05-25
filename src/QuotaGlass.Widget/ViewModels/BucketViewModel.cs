using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.ViewModels;

public sealed class BucketViewModel : INotifyPropertyChanged
{
    private Bucket _model = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Provider => _model.Provider.ToString();

    public string Label => _model.Label;

    public string? Plan => _model.Plan;

    public double Percent => Math.Clamp(_model.Percent, 0, 100);

    public string PercentLabel => $"{Percent:0}% used";

    public string TimeUntilResetLabel
    {
        get
        {
            if (_model.ResetIso is null) return "—";
            var delta = _model.ResetIso.Value - DateTimeOffset.UtcNow;
            if (delta.TotalSeconds <= 0) return "renewed";
            if (delta.TotalDays >= 1) return $"{(int)delta.TotalDays}d {delta.Hours}h";
            if (delta.TotalHours >= 1) return $"{(int)delta.TotalHours}h {delta.Minutes}m";
            if (delta.TotalMinutes >= 1) return $"{(int)delta.TotalMinutes}m {delta.Seconds}s";
            return $"{(int)delta.TotalSeconds}s";
        }
    }

    public string ResetAtLabel
    {
        get
        {
            if (_model.ResetIso is null) return "Reset time unknown";
            var local = _model.ResetIso.Value.ToLocalTime();
            var sameDay = local.Date == DateTimeOffset.Now.Date;
            return sameDay
                ? $"Resets at {local:t}"
                : $"Resets {local:ddd h:mm tt}";
        }
    }

    public string SourceLabel => _model.Source switch
    {
        SnapshotSource.Api => "via API",
        SnapshotSource.Stream => "via SSE",
        SnapshotSource.Headers => "via headers",
        SnapshotSource.Dom => "via DOM",
        SnapshotSource.SilentTab => "via silent tab",
        _ => string.Empty,
    };

    public void Apply(Bucket bucket)
    {
        _model = bucket;
        Raise(nameof(Provider));
        Raise(nameof(Label));
        Raise(nameof(Plan));
        Raise(nameof(Percent));
        Raise(nameof(PercentLabel));
        Raise(nameof(TimeUntilResetLabel));
        Raise(nameof(ResetAtLabel));
        Raise(nameof(SourceLabel));
    }

    public void TickCountdown()
    {
        Raise(nameof(TimeUntilResetLabel));
    }

    private void Raise([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
