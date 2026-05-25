namespace QuotaGlass.NMH;

internal static class Logger
{
    private const long PerFileMaxBytes = 10L * 1024 * 1024;   // 10 MB before rotate
    private const int RetainDays = 14;                         // delete daily files older than this

    private static readonly Lock Gate = new();
    private static string? _logDir;

    public static void Init(string path)
    {
        // R4-Q-04 — store only the directory; recompute the daily file path
        // per write so a process running across midnight rolls into the next
        // day's file naturally.
        _logDir = Path.GetDirectoryName(path);
        try
        {
            if (!string.IsNullOrEmpty(_logDir)) Directory.CreateDirectory(_logDir);
        }
        catch
        {
            // best-effort
        }

        // F-A10: prune ancient daily logs so multi-month users don't
        // accumulate dozens of MB of NMH transcripts.
        PruneOldFiles();
    }

    private static string? CurrentPath()
    {
        if (string.IsNullOrEmpty(_logDir)) return null;
        return Path.Combine(_logDir, $"nmh-{DateTime.Now:yyyy-MM-dd}.log");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:O} [{level}] {message}";
        if (ex is not null)
        {
            line += Environment.NewLine + ex;
        }

        // stderr is safe — Chrome only reads stdout for message framing.
        try
        {
            Console.Error.WriteLine(line);
        }
        catch
        {
            // best-effort
        }

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

            // Roll: foo.log -> foo.log.1 (overwrite any previous .1).
            var rolled = path + ".1";
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(path, rolled);
        }
        catch
        {
            // best-effort
        }
    }

    private static void PruneOldFiles()
    {
        try
        {
            var dir = _logDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow.AddDays(-RetainDays);
            foreach (var file in Directory.EnumerateFiles(dir, "nmh-*.log*"))
            {
                try
                {
                    var ts = File.GetLastWriteTimeUtc(file);
                    if (ts < cutoff) File.Delete(file);
                }
                catch
                {
                    // skip individual file failures
                }
            }
        }
        catch
        {
            // best-effort
        }
    }
}
