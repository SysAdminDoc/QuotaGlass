using System.IO.Compression;
using System.Text.Json.Nodes;
using QuotaGlass.NMH;
using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// R4-N7 — exercises the redaction logic in the diagnostics zip path.
/// <see cref="Diagnostics.Collect"/> is the public entry; we verify the
/// zip contains the expected entries and that orgId / accountId / WAV
/// paths are redacted as documented in CONTRIBUTING / SECURITY.
/// </summary>
public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _backupRoot;

    public DiagnosticsTests()
    {
        // We have to write into the real %LOCALAPPDATA%\QuotaGlass\ because
        // Diagnostics.Collect reads AppPaths static members. To avoid
        // clobbering a developer's real state, save off whatever was there
        // and restore in Dispose.
        _backupRoot = Path.Combine(Path.GetTempPath(), $"qg-diag-backup-{Guid.NewGuid():N}");
        if (Directory.Exists(AppPaths.LocalAppDataRoot))
        {
            Directory.Move(AppPaths.LocalAppDataRoot, _backupRoot);
        }
        Directory.CreateDirectory(AppPaths.LocalAppDataRoot);
        Directory.CreateDirectory(AppPaths.LogsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(AppPaths.LocalAppDataRoot, recursive: true); } catch { }
        if (Directory.Exists(_backupRoot))
        {
            try { Directory.Move(_backupRoot, AppPaths.LocalAppDataRoot); } catch { }
        }
    }

    [Fact]
    public void Collect_creates_zip_with_redactions()
    {
        File.WriteAllText(AppPaths.SnapshotFile, """
        {
          "schemaVersion": 2,
          "ts": "2026-06-01T12:00:00Z",
          "state": {
            "providers": {
              "claude": {
                "ok": true,
                "provider": "claude",
                "orgId": "sensitive-org-12345"
              },
              "codex": {
                "ok": true,
                "provider": "codex",
                "accountId": "sensitive-account-67890"
              }
            }
          }
        }
        """);
        File.WriteAllText(AppPaths.SettingsFile, """
        {
          "alarms": {
            "customWavPath": "C:\\Users\\Alice\\very-long-private-path\\alarm.wav",
            "resetWavPath": "C:\\Users\\Alice\\reset.wav"
          }
        }
        """);
        File.WriteAllText(Path.Combine(AppPaths.LogsDir, "nmh-2026-06-01.log"),
            "INFO sample log line");

        var exitCode = Diagnostics.Collect();
        Assert.Equal(0, exitCode);

        // Find the zip that Diagnostics.Collect wrote.
        var zips = Directory.GetFiles(Path.GetTempPath(), "quotaglass-diag-*.zip");
        var zipPath = zips.OrderByDescending(File.GetLastWriteTimeUtc).First();
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Contains(zip.Entries, e => e.FullName == "meta.txt");
            Assert.Contains(zip.Entries, e => e.FullName == "snapshot.redacted.json");
            Assert.Contains(zip.Entries, e => e.FullName == "settings.redacted.json");
            Assert.Contains(zip.Entries, e => e.FullName.StartsWith("logs/"));

            var snapEntry = zip.Entries.First(e => e.FullName == "snapshot.redacted.json");
            using var sr1 = new StreamReader(snapEntry.Open());
            var snapText = sr1.ReadToEnd();
            Assert.DoesNotContain("sensitive-org-12345", snapText);
            Assert.DoesNotContain("sensitive-account-67890", snapText);
            Assert.Contains("redacted", snapText);

            var settingsEntry = zip.Entries.First(e => e.FullName == "settings.redacted.json");
            using var sr2 = new StreamReader(settingsEntry.Open());
            var settingsText = sr2.ReadToEnd();
            Assert.DoesNotContain("very-long-private-path", settingsText);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }
    }
}
