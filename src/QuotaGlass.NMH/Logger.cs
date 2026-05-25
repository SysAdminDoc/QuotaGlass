namespace QuotaGlass.NMH;

internal static class Logger
{
    private static readonly Lock Gate = new();
    private static string? _path;

    public static void Init(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }
        catch
        {
            // best-effort
        }
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

        if (_path is null) return;

        try
        {
            lock (Gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
