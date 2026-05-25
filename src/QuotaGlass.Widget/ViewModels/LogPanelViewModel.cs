using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.ViewModels;

/// <summary>
/// NX-10 — embedded log-tail surface inside the settings panel. Reads the
/// last <see cref="TailLines"/> from today's NMH + Widget log files every
/// few seconds while the panel is expanded. Read-only; no log injection
/// path.
/// </summary>
public sealed class LogPanelViewModel : INotifyPropertyChanged
{
    public const int TailLines = 24;

    private readonly DispatcherTimer _timer;
    private bool _isExpanded;
    private string _tail = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise();
            Raise(nameof(ToggleLabel));
            if (value)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    public string ToggleLabel => _isExpanded ? "Hide logs" : "Logs";

    public string Tail
    {
        get => _tail;
        private set
        {
            if (_tail == value) return;
            _tail = value;
            Raise();
        }
    }

    public LogPanelViewModel(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Toggle() => IsExpanded = !IsExpanded;

    public void Refresh()
    {
        try
        {
            var dir = AppPaths.LogsDir;
            if (!Directory.Exists(dir)) { Tail = "(no logs yet)"; return; }

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var lines = new List<string>();
            foreach (var name in new[] { $"widget-{date}.log", $"nmh-{date}.log" })
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                lines.AddRange(ReadTail(path, TailLines));
                lines.Add("");
            }

            if (lines.Count == 0) { Tail = "(no log entries today)"; return; }
            Tail = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Tail = $"Log read failed: {ex.Message}";
        }
    }

    private static IEnumerable<string> ReadTail(string path, int count)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var ring = new Queue<string>();
            while (sr.ReadLine() is string line)
            {
                ring.Enqueue(line);
                while (ring.Count > count) ring.Dequeue();
            }
            return new[] { $"-- {Path.GetFileName(path)} --" }.Concat(ring);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void Raise([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
