using System.IO;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Mirrors NMH's logger pattern for the widget process. File-based for
/// post-mortem debugging; daily rotation; 10 MB per-file cap; 14-day
/// retention.
/// </summary>
public static class WidgetLogger
{
    private const long PerFileMaxBytes = 10L * 1024 * 1024;
    private const int RetainDays = 14;

    private static readonly Lock Gate = new();
    private static string? _path;

    public static void Init()
    {
        var path = Path.Combine(AppPaths.LogsDir, $"widget-{DateTime.Now:yyyy-MM-dd}.log");
        try { Directory.CreateDirectory(AppPaths.LogsDir); } catch { }
        _path = path;
        PruneOldFiles();
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:O} [{level}] {message}";
        if (ex is not null) line += Environment.NewLine + ex;

        // Debug build: also write to debug output.
#if DEBUG
        System.Diagnostics.Debug.WriteLine(line);
#endif

        if (_path is null) return;

        try
        {
            lock (Gate)
            {
                RotateIfNeeded();
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static void RotateIfNeeded()
    {
        if (_path is null) return;
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists) return;
            if (info.Length < PerFileMaxBytes) return;
            var rolled = _path + ".1";
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(_path, rolled);
        }
        catch { }
    }

    private static void PruneOldFiles()
    {
        if (_path is null) return;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            var cutoff = DateTime.UtcNow.AddDays(-RetainDays);
            foreach (var file in Directory.EnumerateFiles(dir, "widget-*.log*"))
            {
                try
                {
                    var ts = File.GetLastWriteTimeUtc(file);
                    if (ts < cutoff) File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }
}
