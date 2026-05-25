using QuotaGlass.NMH;
using QuotaGlass.Shared;

AppPaths.EnsureCreated();
Logger.Init(Path.Combine(AppPaths.LogsDir, $"nmh-{DateTime.Now:yyyy-MM-dd}.log"));

if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "--register":
            return HostRegistrar.Register();
        case "--unregister":
            return HostRegistrar.Unregister();
        case "--purge":
            return Purge();
        case "--collect-diagnostics":
            return Diagnostics.Collect();
        case "--version":
            Console.Error.WriteLine($"QuotaGlass.NMH {typeof(Program).Assembly.GetName().Version}");
            return 0;
        case "--help" or "-h" or "/?":
            PrintHelp();
            return 0;
    }
}

// Default: act as a native messaging host. Chrome / Edge / Firefox launch us
// with the calling origin as args[0] (e.g. "chrome-extension://abcdef.../" ).
var callerOrigin = args.Length > 0 ? args[0] : "(no-origin)";
Logger.Info($"NMH started, caller={callerOrigin}, pid={Environment.ProcessId}");

try
{
    var pump = new MessagePump(callerOrigin);
    return await pump.RunAsync();
}
catch (Exception ex)
{
    Logger.Error("Fatal in MessagePump", ex);
    return 2;
}

static int Purge()
{
    // R-Rec-02: wipe %LOCALAPPDATA%\QuotaGlass\* (preserves the folder
    // itself; deletes snapshot, settings, logs, sounds, manifests).
    try
    {
        var root = AppPaths.LocalAppDataRoot;
        if (Directory.Exists(root))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                try
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                    else File.Delete(entry);
                }
                catch (Exception inner)
                {
                    Console.Error.WriteLine($"warn: failed to delete {entry}: {inner.Message}");
                }
            }
        }
        Console.Error.WriteLine($"Purged {root} (folder retained).");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Purge failed: {ex.Message}");
        return 1;
    }
}

static void PrintHelp()
{
    Console.Error.WriteLine("""
        QuotaGlass.NMH — native messaging host for the AI-Usage_Tracker extension.

        Usage:
          QuotaGlass.NMH.exe                # run as native messaging host (stdin/stdout)
          QuotaGlass.NMH.exe --register     # install registry keys + manifest for Chrome/Edge/Firefox
          QuotaGlass.NMH.exe --unregister   # remove registry keys + manifest
          QuotaGlass.NMH.exe --purge        # wipe %LOCALAPPDATA%\QuotaGlass\ (does not unregister)
          QuotaGlass.NMH.exe --collect-diagnostics  # zip logs + redacted snapshot/settings into %TEMP%\quotaglass-diag-*.zip
          QuotaGlass.NMH.exe --version      # print version and exit
        """);
}

internal partial class Program;
