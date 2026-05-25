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
    private static bool _initialized;

    public static void Init()
    {
        try { Directory.CreateDirectory(AppPaths.LogsDir); } catch { }
        _initialized = true;
        PruneOldFiles();
    }

    private static string? CurrentPath()
    {
        // R4-Q-04 — recompute per write so a widget running across midnight
        // doesn't keep appending to yesterday's file.
        return _initialized
            ? Path.Combine(AppPaths.LogsDir, $"widget-{DateTime.Now:yyyy-MM-dd}.log")
            : null;
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

        var path = CurrentPath();
        if (path is null) return;

        try
        {
            lock (Gate)
            {
                RotateIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            if (info.Length < PerFileMaxBytes) return;
            var rolled = path + ".1";
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(path, rolled);
        }
        catch { }
    }

    private static void PruneOldFiles()
    {
        if (!_initialized) return;
        try
        {
            var dir = AppPaths.LogsDir;
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
