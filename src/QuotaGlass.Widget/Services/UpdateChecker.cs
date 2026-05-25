using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Self-hosted updater. Queries the GitHub Releases API for the latest
/// tag, downloads the matching arch asset to <c>%TEMP%</c>, writes a
/// PowerShell self-replace script, and launches it. Pattern adapted from
/// Zrnik/claude-usage-windows-taskbar-widget (MIT).
///
/// Tradeoffs vs Velopack:
///   + zero dependencies, ~150 LOC
///   + matches QuotaGlass's "small + auditable" ethos
///   - no delta packages (full re-download each update; we're ~600 KB)
///   - no staged rollouts
///   - PS1 replace is racey if AV has the EXE locked
/// </summary>
[SupportedOSPlatform("windows")]
public static class UpdateChecker
{
    private const string Repo = "SysAdminDoc/QuotaGlass";
    private const string LatestApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuotaGlass", CurrentVersion));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public sealed record UpdateInfo(string LatestVersion, string DownloadUrl, string AssetName);

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(LatestApiUrl, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var tag = node["tag_name"]?.GetValue<string>() ?? string.Empty;
            var latest = tag.TrimStart('v');
            if (string.IsNullOrEmpty(latest)) return null;
            if (!IsNewer(latest, CurrentVersion)) return null;

            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            var wantedSuffix = $"-win-{arch}.exe";
            var assets = node["assets"]?.AsArray();
            if (assets is null) return null;

            foreach (var asset in assets)
            {
                var name = asset?["name"]?.GetValue<string>() ?? string.Empty;
                if (name.EndsWith(wantedSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset?["browser_download_url"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(url))
                    {
                        return new UpdateInfo(latest, url!, name);
                    }
                }
            }
            return null;
        }
        catch
        {
            // Offline / rate-limited / repo renamed — silently bail.
            return null;
        }
    }

    public static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
        {
            return l > c;
        }
        return !string.Equals(latest, current, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spawn a PowerShell script that downloads the update, kills the
    /// current process, copies the EXE in place, and relaunches. Returns
    /// immediately; the script handles the rest.
    /// </summary>
    public static void LaunchSelfReplace(UpdateInfo update)
    {
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("ProcessPath is null");
        var currentPid = Environment.ProcessId;
        var tempExe = Path.Combine(Path.GetTempPath(), "QuotaGlass_update.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), "QuotaGlass_update.ps1");

        var script =
            "Write-Host 'Downloading QuotaGlass update...' -ForegroundColor Cyan\r\n" +
            $"$ProgressPreference = 'SilentlyContinue'\r\n" +
            $"Invoke-WebRequest -UseBasicParsing -Uri '{update.DownloadUrl}' -OutFile '{tempExe}'\r\n" +
            "Write-Host 'Stopping QuotaGlass...' -ForegroundColor Yellow\r\n" +
            $"Stop-Process -Id {currentPid} -Force -ErrorAction SilentlyContinue\r\n" +
            "Start-Sleep -Milliseconds 800\r\n" +
            "Write-Host 'Installing update...' -ForegroundColor Yellow\r\n" +
            $"Copy-Item '{tempExe}' '{currentExe}' -Force\r\n" +
            $"Remove-Item '{tempExe}' -Force -ErrorAction SilentlyContinue\r\n" +
            "Write-Host 'Relaunching...' -ForegroundColor Green\r\n" +
            $"Start-Process '{currentExe}'\r\n" +
            "Start-Sleep -Seconds 2\r\n" +
            $"Remove-Item '{scriptPath}' -Force -ErrorAction SilentlyContinue\r\n";

        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Minimized,
        });
    }
}
