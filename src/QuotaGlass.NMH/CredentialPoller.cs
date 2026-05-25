using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// F-N1 — Direct OAuth-credential reader for the Claude Code, Codex, and
/// Hermes CLIs. Runs alongside the extension-driven snapshot path and
/// fills the gap when the browser is closed.
///
/// Behavior:
/// 1. Read each credential file under <c>%USERPROFILE%</c> if present.
/// 2. Issue a minimal API request to the matching provider to extract the
///    rate-limit headers ("anthropic-ratelimit-unified-5h-utilization" and
///    "anthropic-ratelimit-unified-7d-utilization" for Claude;
///    "x-codex-ratelimit-unified-*" for Codex).
/// 3. Synthesize a <see cref="SnapshotMessage"/> with the resulting bucket
///    percentages and write it via <see cref="AtomicJsonFile"/> — same
///    sink the message-pump uses. The extension-side flow takes precedence
///    when both produce data; whichever wrote more recently wins.
///
/// Schedule:
///   <c>QuotaGlass.NMH.exe --poll-credentials [--interval-minutes N]</c>
/// launches a long-running poll loop. Users with no Claude Code / Codex CLI
/// install pay zero cost (file probe is a single stat).
/// </summary>
public sealed class CredentialPoller
{
    private const string AnthropicMessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string OpenAiUsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string Source = "local-creds";

    private readonly HttpClient _http;
    private readonly TimeSpan _interval;

    public CredentialPoller(TimeSpan interval)
    {
        _interval = interval;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuotaGlass.NMH", HostMetadata.Version));
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        Logger.Info($"--poll-credentials started; interval={_interval}");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("credential poll iteration failed", ex);
            }

            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        Logger.Info("--poll-credentials exiting");
        return 0;
    }

    public async Task PollOnceAsync(CancellationToken ct)
    {
        var providers = new ProviderMap();
        var any = false;

        foreach (var probe in EnumerateClaudeCredentialFiles())
        {
            var claude = await ProbeClaudeAsync(probe, ct).ConfigureAwait(false);
            if (claude is not null)
            {
                providers.Claude = claude;
                any = true;
                break;
            }
        }

        foreach (var probe in EnumerateCodexCredentialFiles())
        {
            var codex = await ProbeCodexAsync(probe, ct).ConfigureAwait(false);
            if (codex is not null)
            {
                providers.Codex = codex;
                any = true;
                break;
            }
        }

        if (!any)
        {
            Logger.Info("no local credential files found; nothing to write");
            return;
        }

        var envelope = new SnapshotMessage
        {
            Kind = "snapshot",
            SchemaVersion = SchemaVersion.Current,
            Timestamp = DateTimeOffset.UtcNow,
            ExtensionVersion = $"local-creds/{HostMetadata.Version}",
            State = new ExtensionState
            {
                FetchedAtIso = DateTimeOffset.UtcNow,
                Providers = providers,
            },
        };

        try
        {
            AtomicJsonFile.Write(AppPaths.SnapshotFile, envelope, SnapshotJsonContext.Default.SnapshotMessage);
            var claudeCount = providers.Claude?.Buckets?.Count ?? 0;
            var codexCount = providers.Codex?.Buckets?.Count ?? 0;
            Logger.Info($"credential poll wrote snapshot — claude={claudeCount} codex={codexCount}");
        }
        catch (Exception ex)
        {
            Logger.Error("credential-poll snapshot write failed", ex);
        }
    }

    // ----- Claude Code / Hermes credential paths --------------------------

    public static IEnumerable<string> EnumerateClaudeCredentialFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) yield break;
        var primary = Path.Combine(home, ".claude", ".credentials.json");
        if (File.Exists(primary)) yield return primary;
        var hermes = Path.Combine(home, ".hermes", "auth.json");
        if (File.Exists(hermes)) yield return hermes;
    }

    public static IEnumerable<string> EnumerateCodexCredentialFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) yield break;
        var primary = Path.Combine(home, ".codex", "auth.json");
        if (File.Exists(primary)) yield return primary;
    }

    /// <summary>
    /// Extracts the OAuth access token from a Claude Code / Hermes
    /// credentials JSON. Both files use a similar shape; we try a small
    /// set of well-known field names and fall back to <c>null</c> if none
    /// match — schema may drift across CLI versions.
    /// </summary>
    public static string? ExtractAccessToken(string path)
    {
        try
        {
            var raw = File.ReadAllText(path);
            var root = JsonNode.Parse(raw);
            if (root is not JsonObject obj) return null;

            string? Pick(JsonObject scope, params string[] names)
            {
                foreach (var name in names)
                {
                    if (scope[name]?.GetValue<string?>() is { Length: > 0 } v) return v;
                }
                return null;
            }

            // Top-level shapes seen in the wild.
            var token = Pick(obj, "access_token", "accessToken", "token", "apiKey", "api_key");
            if (!string.IsNullOrEmpty(token)) return token;

            // Nested under "credentials" / "tokens" / "auth".
            foreach (var key in new[] { "credentials", "tokens", "auth" })
            {
                if (obj[key] is JsonObject nested)
                {
                    var t = Pick(nested, "access_token", "accessToken", "token", "apiKey", "api_key");
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
        }
        catch
        {
            // Schema may drift between CLI versions; treat unparseable as "no token".
        }
        return null;
    }

    private async Task<ProviderSnapshot?> ProbeClaudeAsync(string path, CancellationToken ct)
    {
        var token = ExtractAccessToken(path);
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            // Minimal /v1/messages POST: we don't care about the body, just
            // the rate-limit response headers. A 1-token "ping" message keeps
            // billing impact ~zero.
            using var req = new HttpRequestMessage(HttpMethod.Post, AnthropicMessagesEndpoint);
            if (token.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.Add("x-api-key", token);
            }
            else
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(
                "{\"model\":\"claude-haiku-4-5-20251001\",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return ExtractClaudeBuckets(resp.Headers, resp.Content.Headers);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Claude credential probe failed for {Path.GetFileName(path)}: {ex.Message}");
            return new ProviderSnapshot
            {
                Ok = false,
                Provider = "claude",
                Source = Source,
                Error = "credential-probe-failed",
            };
        }
    }

    private async Task<ProviderSnapshot?> ProbeCodexAsync(string path, CancellationToken ct)
    {
        var token = ExtractAccessToken(path);
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, OpenAiUsageEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractCodexBuckets(body, resp.Headers);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Codex credential probe failed for {Path.GetFileName(path)}: {ex.Message}");
            return new ProviderSnapshot
            {
                Ok = false,
                Provider = "codex",
                Source = Source,
                Error = "credential-probe-failed",
            };
        }
    }

    // ----- Header parsing (also called by unit tests) ---------------------

    /// <summary>
    /// Reads <c>anthropic-ratelimit-unified-{5h,7d}-utilization</c> from
    /// the response headers and synthesizes per-bucket data. Both header
    /// shapes return a 0..1 float (NOT 0..100). Missing headers ⇒ no bucket.
    /// </summary>
    public static ProviderSnapshot ExtractClaudeBuckets(System.Net.Http.Headers.HttpResponseHeaders responseHeaders,
                                                       System.Net.Http.Headers.HttpContentHeaders contentHeaders)
    {
        var snap = new ProviderSnapshot
        {
            Ok = true,
            Provider = "claude",
            Source = Source,
        };

        string? FirstHeader(string name)
        {
            if (responseHeaders.TryGetValues(name, out var v1)) return v1.FirstOrDefault();
            if (contentHeaders.TryGetValues(name, out var v2)) return v2.FirstOrDefault();
            return null;
        }

        var sessionUtil = FirstHeader("anthropic-ratelimit-unified-5h-utilization");
        if (TryParseRatio(sessionUtil, out var sessionPercent))
        {
            var resetRaw = FirstHeader("anthropic-ratelimit-unified-5h-reset");
            snap.Buckets.Add(new Bucket
            {
                Id = "claude-session",
                Kind = "session",
                Model = "all",
                Label = "Claude 5h session",
                PercentUsed = sessionPercent,
                ResetIso = ParseEpochOrIso(resetRaw),
            });
        }

        var weeklyUtil = FirstHeader("anthropic-ratelimit-unified-7d-utilization");
        if (TryParseRatio(weeklyUtil, out var weeklyPercent))
        {
            var resetRaw = FirstHeader("anthropic-ratelimit-unified-7d-reset");
            snap.Buckets.Add(new Bucket
            {
                Id = "claude-weekly-all",
                Kind = "weekly",
                Model = "all",
                Label = "Claude weekly",
                PercentUsed = weeklyPercent,
                ResetIso = ParseEpochOrIso(resetRaw),
            });
        }

        if (snap.Buckets.Count == 0)
        {
            snap.Ok = false;
            snap.Error = "no-rate-limit-headers";
        }
        return snap;
    }

    /// <summary>
    /// Parses ChatGPT's WHAM usage JSON. Codex CLI exposes the same shape.
    /// Looks for <c>primary_window</c> (5h) and <c>secondary_window</c>
    /// (weekly) with <c>used_percent</c> and <c>resets_at</c>.
    /// </summary>
    public static ProviderSnapshot ExtractCodexBuckets(string body, System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var snap = new ProviderSnapshot
        {
            Ok = true,
            Provider = "codex",
            Source = Source,
        };

        try
        {
            if (JsonNode.Parse(body) is JsonObject root)
            {
                AppendCodexBucket(snap, root["primary_window"] as JsonObject,
                    id: "codex-5h-all", kind: "5h", label: "Codex 5-hour limit");
                AppendCodexBucket(snap, root["secondary_window"] as JsonObject,
                    id: "codex-weekly-all", kind: "weekly", label: "Codex weekly limit");
            }
        }
        catch
        {
            // Best-effort; fall through to "no-bucket" branch.
        }

        if (snap.Buckets.Count == 0)
        {
            snap.Ok = false;
            snap.Error = "no-wham-buckets";
        }
        return snap;
    }

    private static void AppendCodexBucket(ProviderSnapshot snap, JsonObject? window,
                                          string id, string kind, string label)
    {
        if (window is null) return;
        var used = window["used_percent"]?.GetValue<double?>()
                   ?? window["utilization"]?.GetValue<double?>();
        if (used is null) return;

        // ChatGPT WHAM exposes 0..1 utilization; legacy used_percent is 0..100.
        var percent = used.Value <= 1.0 ? used.Value * 100 : used.Value;
        var resetRaw = window["resets_at"]?.GetValue<string?>()
                     ?? window["reset_at"]?.GetValue<string?>();
        snap.Buckets.Add(new Bucket
        {
            Id = id,
            Kind = kind,
            Model = "all",
            Label = label,
            PercentUsed = percent,
            ResetIso = ParseEpochOrIso(resetRaw),
        });
    }

    public static bool TryParseRatio(string? raw, out double percent)
    {
        percent = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
        // Headers ship as 0..1 ratios.
        percent = v <= 1.0 ? v * 100 : v;
        return true;
    }

    public static DateTimeOffset? ParseEpochOrIso(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var epoch))
        {
            // Heuristic: epochs > 10^12 are milliseconds, otherwise seconds.
            return epoch > 1_000_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.ToUniversalTime();
        }
        return null;
    }
}
