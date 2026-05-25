using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// R3-P1-02 — `QuotaGlass.NMH.exe --collect-diagnostics` zips logs,
/// redacted snapshot, redacted settings, and a meta.txt into
/// <c>%TEMP%\quotaglass-diag-{ts}.zip</c> so users filing issues can
/// attach a single artifact instead of hunting down four folders.
///
/// Redaction rules:
///   - <c>orgId</c>, <c>accountId</c> → <c>"redacted"</c>
///   - <c>customWavPath</c>, <c>resetWavPath</c>, <c>zeroStateWavPath</c> →
///     last 12 chars only (preserves diagnostic value: extension + filename
///     hint, no full path leak).
/// </summary>
public static class Diagnostics
{
    public static int Collect()
    {
        try
        {
            var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var outPath = Path.Combine(Path.GetTempPath(), $"quotaglass-diag-{ts}.zip");

            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddMeta(zip);
                AddLogs(zip);
                AddRedactedSnapshot(zip);
                AddRedactedLocalCredsSnapshot(zip); // R5-P0-01 — Pass 5 Bug 1
                AddRedactedSettings(zip);
                AddFiredRules(zip);
            }

            Console.Error.WriteLine($"Diagnostics bundle written to: {outPath}");
            Console.WriteLine(outPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Diagnostics collection failed: {ex.Message}");
            return 1;
        }
    }

    private static void AddMeta(ZipArchive zip)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"QuotaGlass diagnostics");
        sb.AppendLine($"Generated:        {DateTime.UtcNow:O}");
        sb.AppendLine($"NMH version:      {HostMetadata.Version}");
        sb.AppendLine($"Schema range:     {SchemaVersion.Min}..{SchemaVersion.Max}");
        sb.AppendLine($"OS:               {Environment.OSVersion}");
        sb.AppendLine($"Process arch:     {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"LocalAppData:     {AppPaths.LocalAppDataRoot}");
        sb.AppendLine($"Snapshot exists:  {File.Exists(AppPaths.SnapshotFile)}");
        sb.AppendLine($"Settings exists:  {File.Exists(AppPaths.SettingsFile)}");
        sb.AppendLine();
        sb.AppendLine("Native-messaging registration (HKCU):");
        foreach (var (browser, sub) in new[]
        {
            ("Chrome",   @"Software\Google\Chrome\NativeMessagingHosts\com.sysadmindoc.quotaglass"),
            ("Edge",     @"Software\Microsoft\Edge\NativeMessagingHosts\com.sysadmindoc.quotaglass"),
            ("Chromium", @"Software\Chromium\NativeMessagingHosts\com.sysadmindoc.quotaglass"),
            ("Firefox",  @"Software\Mozilla\NativeMessagingHosts\com.sysadmindoc.quotaglass"),
        })
        {
            sb.AppendLine($"  {browser,-9}: {(KeyExists(sub) ? "registered" : "missing")}");
        }

        WriteEntry(zip, "meta.txt", sb.ToString());
    }

    private static bool KeyExists(string subKey)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void AddLogs(ZipArchive zip)
    {
        if (!Directory.Exists(AppPaths.LogsDir)) return;
        foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDir))
        {
            try
            {
                var entry = zip.CreateEntry($"logs/{Path.GetFileName(file)}", CompressionLevel.Optimal);
                using var srcStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dstStream = entry.Open();
                srcStream.CopyTo(dstStream);
            }
            catch
            {
                // Best-effort — skip files that are locked or unreadable.
            }
        }
    }

    private static void AddRedactedSnapshot(ZipArchive zip)
    {
        if (!File.Exists(AppPaths.SnapshotFile)) return;
        try
        {
            var raw = File.ReadAllText(AppPaths.SnapshotFile);
            var node = JsonNode.Parse(raw);
            RedactSnapshotIdentifiers(node);
            WriteEntry(zip, "snapshot.redacted.json", node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? raw);
        }
        catch (Exception ex)
        {
            WriteEntry(zip, "snapshot.read-error.txt", $"Failed to redact snapshot: {ex.Message}");
        }
    }

    private static void AddRedactedLocalCredsSnapshot(ZipArchive zip)
    {
        // R5-P0-01 — same redaction as AddRedactedSnapshot, but reads the
        // sibling file written by `--poll-credentials` (R4-P1-02). Skipped
        // silently when no credential-poll producer is configured.
        if (!File.Exists(AppPaths.LocalCredsSnapshotFile)) return;
        try
        {
            var raw = File.ReadAllText(AppPaths.LocalCredsSnapshotFile);
            var node = JsonNode.Parse(raw);
            RedactSnapshotIdentifiers(node);
            WriteEntry(zip, "snapshot.local-creds.redacted.json",
                node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? raw);
        }
        catch (Exception ex)
        {
            WriteEntry(zip, "snapshot.local-creds.read-error.txt",
                $"Failed to redact local-creds snapshot: {ex.Message}");
        }
    }

    private static void AddRedactedSettings(ZipArchive zip)
    {
        if (!File.Exists(AppPaths.SettingsFile)) return;
        try
        {
            var raw = File.ReadAllText(AppPaths.SettingsFile);
            var node = JsonNode.Parse(raw);
            RedactSettingsPaths(node);
            WriteEntry(zip, "settings.redacted.json", node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? raw);
        }
        catch (Exception ex)
        {
            WriteEntry(zip, "settings.read-error.txt", $"Failed to redact settings: {ex.Message}");
        }
    }

    private static void AddFiredRules(ZipArchive zip)
    {
        var path = Path.Combine(AppPaths.LocalAppDataRoot, "fired-rules.json");
        if (!File.Exists(path)) return;
        try
        {
            // No PII here — just opaque rule keys + epoch timestamps.
            using var srcStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var entry = zip.CreateEntry("fired-rules.json", CompressionLevel.Optimal);
            using var dstStream = entry.Open();
            srcStream.CopyTo(dstStream);
        }
        catch { /* best-effort */ }
    }

    private static void RedactSnapshotIdentifiers(JsonNode? root)
    {
        if (root is not JsonObject envelope) return;
        if (envelope["state"] is not JsonObject state) return;
        if (state["providers"] is not JsonObject providers) return;

        foreach (var providerName in new[] { "claude", "codex" })
        {
            if (providers[providerName] is not JsonObject p) continue;
            if (p["orgId"] is not null) p["orgId"] = "redacted";
            if (p["accountId"] is not null) p["accountId"] = "redacted";
        }
    }

    private static void RedactSettingsPaths(JsonNode? root)
    {
        if (root is not JsonObject settings) return;
        if (settings["alarms"] is not JsonObject alarms) return;
        foreach (var prop in new[] { "customWavPath", "resetWavPath", "zeroStateWavPath" })
        {
            if (alarms[prop]?.GetValue<string?>() is string s && !string.IsNullOrEmpty(s))
            {
                alarms[prop] = s.Length <= 12 ? s : "…" + s[^12..];
            }
        }
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.Write(content);
    }
}
