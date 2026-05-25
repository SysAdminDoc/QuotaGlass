namespace QuotaGlass.Shared;

public static class AppPaths
{
    public static string LocalAppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuotaGlass");

    public static string SnapshotFile { get; } = Path.Combine(LocalAppDataRoot, "snapshot.json");

    /// <summary>
    /// R4-P1-02 — sibling snapshot file written by `--poll-credentials`.
    /// SnapshotWatcher merges this with the canonical SnapshotFile so the
    /// extension chain and the credential poller don't race for the same
    /// path.
    /// </summary>
    public static string LocalCredsSnapshotFile { get; } = Path.Combine(LocalAppDataRoot, "snapshot.local-creds.json");

    public static string SettingsFile { get; } = Path.Combine(LocalAppDataRoot, "settings.json");

    public static string SoundsDir { get; } = Path.Combine(LocalAppDataRoot, "sounds");

    public static string LogsDir { get; } = Path.Combine(LocalAppDataRoot, "logs");

    public static string NmhManifestFile { get; } = Path.Combine(LocalAppDataRoot, "nmh-manifest.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(LocalAppDataRoot);
        Directory.CreateDirectory(SoundsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
