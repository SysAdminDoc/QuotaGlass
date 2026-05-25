using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// F-N1 — direct OAuth-credential reader for Claude Code / Codex / Hermes
/// CLIs. Runs alongside the extension-driven snapshot path; closes the
/// "browser must be open" gap for users with the CLIs installed.
///
/// Endpoint routing (post Pass 4 / R4-P0-01..04):
///
///   Claude Code OAuth token   → GET api.claude.ai/api/organizations/{orgId}/usage
///                               Bearer auth; parses
///                               anthropic-ratelimit-unified-{5h,7d}-utilization
///                               from response headers. Same path the extension
///                               scrapes via session cookies.
///   sk-ant-… admin key        → unsupported in v0.5 (Admin API is workspace-
///                               billing scope, not per-window utilization).
///                               Logged as detail="unsupported-token-type".
///   Codex sk-… OpenAI key     → GET api.openai.com/v1/usage today
///                               (daily token counts only — 5h / weekly
///                               window data is not exposed by the OpenAI
///                               Platform API). Marked Ok=false with detail
///                               so the widget renders the gap honestly.
///   Codex ChatGPT session     → unsupported (cookie-auth only). Logged.
///
/// OAuth refresh: Claude Code credentials carry a `refresh_token`. On 401
/// we POST to the refresh endpoint, update the in-memory token cache, retry
/// once. We DO NOT write back to the user's `.credentials.json` — the CLI
/// owns that file; conflicting writes would corrupt it.
/// </summary>
public sealed class CredentialPoller
{
    // Documented in docs/extension-integration.md §Direct credential reading.
    private const string ClaudeUsageEndpointTemplate = "https://api.claude.ai/api/organizations/{0}/usage";
    private const string ClaudeOAuthRefreshEndpoint = "https://console.anthropic.com/v1/oauth/token";
    private const string OpenAiUsageEndpoint = "https://api.openai.com/v1/usage";
    private const string Source = "local-creds";

    private readonly HttpClient _http;
    private readonly TimeSpan _interval;
    private readonly Dictionary<string, OAuthCache> _oauth = new();

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

        // R4-P1-02 — write to a sibling path so the extension-chain
        // producer and `--poll-credentials` don't race the canonical
        // snapshot.json. The widget's SnapshotWatcher merges both.
        try
        {
            AtomicJsonFile.Write(AppPaths.LocalCredsSnapshotFile, envelope, SnapshotJsonContext.Default.SnapshotMessage);
            var claudeCount = providers.Claude?.Buckets?.Count ?? 0;
            var codexCount = providers.Codex?.Buckets?.Count ?? 0;
            Logger.Info($"credential poll wrote local-creds snapshot — claude={claudeCount} codex={codexCount}");
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
    /// Pulls the access token, refresh token (if any), org/account id from
    /// a credentials JSON. Tolerant against the various shapes seen in the
    /// wild (top-level, nested under credentials/tokens/auth, etc.).
    /// </summary>
    public static CredentialFile? ReadCredentialFile(string path)
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

            var access = Pick(obj, "access_token", "accessToken", "token", "apiKey", "api_key");
            var refresh = Pick(obj, "refresh_token", "refreshToken");
            var orgId = Pick(obj, "organization_id", "organizationId", "orgId", "org_id");
            var accountId = Pick(obj, "account_id", "accountId", "user_id", "userId");

            foreach (var key in new[] { "credentials", "tokens", "auth" })
            {
                if (obj[key] is JsonObject nested)
                {
                    access ??= Pick(nested, "access_token", "accessToken", "token", "apiKey", "api_key");
                    refresh ??= Pick(nested, "refresh_token", "refreshToken");
                    orgId ??= Pick(nested, "organization_id", "organizationId", "orgId", "org_id");
                    accountId ??= Pick(nested, "account_id", "accountId", "user_id", "userId");
                }
            }

            if (string.IsNullOrEmpty(access)) return null;
            return new CredentialFile(access, refresh, orgId, accountId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compatibility shim — older tests call this directly.
    /// </summary>
    public static string? ExtractAccessToken(string path) => ReadCredentialFile(path)?.AccessToken;

    /// <summary>
    /// Classify a token by syntactic shape so we can pick the right endpoint
    /// without making any network calls.
    /// </summary>
    public static TokenKind ClassifyToken(string? token) => token switch
    {
        null or "" => TokenKind.Unknown,
        _ when token.StartsWith("sk-ant-admin-", StringComparison.OrdinalIgnoreCase) => TokenKind.AnthropicAdminKey,
        _ when token.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase) => TokenKind.AnthropicApiKey,
        _ when token.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) => TokenKind.OpenAiApiKey,
        _ when token.Length >= 32 => TokenKind.OAuthBearer,
        _ => TokenKind.Unknown,
    };

    private async Task<ProviderSnapshot?> ProbeClaudeAsync(string path, CancellationToken ct)
    {
        var creds = ReadCredentialFile(path);
        if (creds is null) return null;

        var kind = ClassifyToken(creds.AccessToken);

        // R4-P0-01 — Claude Code OAuth tokens validate ONLY against the
        // consumer endpoint. Admin keys / sk-ant- API keys don't expose
        // per-window utilization headers, so we surface a clear error
        // rather than burning tokens against /v1/messages.
        if (kind != TokenKind.OAuthBearer)
        {
            Logger.Info($"Claude credential at {Path.GetFileName(path)} has token kind={kind}; only OAuth tokens are supported in v0.5");
            return new ProviderSnapshot
            {
                Ok = false,
                Provider = "claude",
                Source = Source,
                OrgId = creds.OrgId,
                Error = "unsupported-token-type",
            };
        }

        if (string.IsNullOrEmpty(creds.OrgId))
        {
            Logger.Warn($"Claude credential at {Path.GetFileName(path)} is missing organization id; cannot probe usage endpoint");
            return new ProviderSnapshot
            {
                Ok = false,
                Provider = "claude",
                Source = Source,
                Error = "missing-org-id",
            };
        }

        try
        {
            var snap = await SendClaudeProbeAsync(creds, path, ct).ConfigureAwait(false);
            return snap;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Claude credential probe failed for {Path.GetFileName(path)}: {ex.Message}");
            return new ProviderSnapshot
            {
                Ok = false,
                Provider = "claude",
                Source = Source,
                OrgId = creds.OrgId,
                Error = "credential-probe-failed",
            };
        }
    }

    private async Task<ProviderSnapshot> SendClaudeProbeAsync(CredentialFile creds, string path, CancellationToken ct)
    {
        var url = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            ClaudeUsageEndpointTemplate, creds.OrgId);

        var token = GetCachedAccessToken(path, creds);
        using var resp = await SendClaudeRequestAsync(url, token, ct).ConfigureAwait(false);

        // R4-N1 — OAuth refresh on expired-token 401.
        if (resp.StatusCode == HttpStatusCode.Unauthorized
            && !string.IsNullOrEmpty(creds.RefreshToken))
        {
            Logger.Info("Claude probe got 401; attempting OAuth refresh");
            var refreshed = await RefreshAccessTokenAsync(creds.RefreshToken!, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(refreshed))
            {
                _oauth[path] = new OAuthCache(refreshed!, DateTimeOffset.UtcNow.AddMinutes(55));
                using var retry = await SendClaudeRequestAsync(url, refreshed!, ct).ConfigureAwait(false);
                return await ParseClaudeResponseAsync(retry, creds, ct).ConfigureAwait(false);
            }
        }

        return await ParseClaudeResponseAsync(resp, creds, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendClaudeRequestAsync(string url, string token, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-client", $"QuotaGlass/{HostMetadata.Version}");
        return await _http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<ProviderSnapshot> ParseClaudeResponseAsync(HttpResponseMessage resp, CredentialFile creds, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            Logger.Warn($"Claude usage endpoint returned {(int)resp.StatusCode}");
            return new ProviderSnapshot
            {
                Ok = false,
                Provider = "claude",
                Source = Source,
                OrgId = creds.OrgId,
                Error = $"http-{(int)resp.StatusCode}",
            };
        }

        var snap = ExtractClaudeBuckets(resp.Headers, resp.Content.Headers);
        snap.OrgId = creds.OrgId;

        // Some Claude usage responses include the bucket data in the body
        // too; we ignore the body in v0.5 and rely on headers, but read
        // the response stream so the connection can be reused.
        _ = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return snap;
    }

    private string GetCachedAccessToken(string path, CredentialFile creds)
    {
        if (_oauth.TryGetValue(path, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.AccessToken;
        }
        _oauth[path] = new OAuthCache(creds.AccessToken, DateTimeOffset.UtcNow.AddMinutes(55));
        return creds.AccessToken;
    }

    /// <summary>
    /// R4-N1 — exchange a refresh_token for a new access_token. Returns the
    /// new access_token on success, null on failure. Never throws.
    /// </summary>
    private async Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, ClaudeOAuthRefreshEndpoint);
            var body = $"{{\"grant_type\":\"refresh_token\",\"refresh_token\":\"{System.Web.HttpUtility.JavaScriptStringEncode(refreshToken)}\"}}";
            req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Warn($"OAuth refresh returned {(int)resp.StatusCode}");
                return null;
            }
            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(raw);
            return node?["access_token"]?.GetValue<string?>();
        }
        catch (Exception ex)
        {
            Logger.Warn($"OAuth refresh failed: {ex.Message}");
            return null;
        }
    }

    private async Task<ProviderSnapshot?> ProbeCodexAsync(string path, CancellationToken ct)
    {
        var creds = ReadCredentialFile(path);
        if (creds is null) return null;

        var kind = ClassifyToken(creds.AccessToken);

        // R4-P0-02 — Codex CLI tokens come in three flavors we observe:
        //   sk-…       → OpenAI Platform API key → /v1/usage (daily only).
        //   OAuth      → ChatGPT browser session token → cookie-auth only,
        //                unsupported without scraping Chromium cookies
        //                (rejected in Pass 1 §3 Option B).
        //   anything   → unknown shape.
        if (kind == TokenKind.OpenAiApiKey)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{OpenAiUsageEndpoint}?date={DateTime.UtcNow:yyyy-MM-dd}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    return new ProviderSnapshot
                    {
                        Ok = false,
                        Provider = "codex",
                        Source = Source,
                        AccountId = creds.AccountId,
                        Error = $"http-{(int)resp.StatusCode}",
                    };
                }
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ExtractOpenAiPlatformUsage(body, creds.AccountId);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Codex credential probe failed for {Path.GetFileName(path)}: {ex.Message}");
                return new ProviderSnapshot
                {
                    Ok = false,
                    Provider = "codex",
                    Source = Source,
                    AccountId = creds.AccountId,
                    Error = "credential-probe-failed",
                };
            }
        }

        Logger.Info($"Codex credential at {Path.GetFileName(path)} has token kind={kind}; unsupported in v0.5");
        return new ProviderSnapshot
        {
            Ok = false,
            Provider = "codex",
            Source = Source,
            AccountId = creds.AccountId,
            Error = "unsupported-token-type",
        };
    }

    // ----- Header / body parsing (also called by unit tests) --------------

    /// <summary>
    /// Reads <c>anthropic-ratelimit-unified-{5h,7d}-utilization</c> from the
    /// consumer usage endpoint response and synthesizes per-bucket data.
    /// Headers are 0..1 ratios. Missing headers ⇒ no bucket; the snapshot
    /// is flagged Ok=false so the widget renders the gap honestly.
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
    /// Parses the OpenAI Platform usage endpoint response. v0.5 surfaces
    /// the daily total only — 5h / weekly window data is not exposed by
    /// the Platform API for ChatGPT / Codex personal accounts.
    /// </summary>
    public static ProviderSnapshot ExtractOpenAiPlatformUsage(string body, string? accountId)
    {
        var snap = new ProviderSnapshot
        {
            Ok = true,
            Provider = "codex",
            Source = Source,
            AccountId = accountId,
        };

        try
        {
            if (JsonNode.Parse(body) is JsonObject root)
            {
                var total = root["total_usage"]?.GetValue<double?>()
                            ?? root["total"]?.GetValue<double?>();
                if (total is not null)
                {
                    // No absolute cap published; OpenAI Platform usage is
                    // dollar-based or token-based depending on subscription.
                    // Until a real quota source surfaces, show as 0-100
                    // capped at 100 for the visual ring; v0.6 will pull
                    // the actual cap from /v1/dashboard/billing/credit_grants.
                    var percent = Math.Min(100, total.Value / 10_000.0 * 100);
                    snap.Buckets.Add(new Bucket
                    {
                        Id = "codex-platform-daily",
                        Kind = "5h",
                        Model = "all",
                        Label = "Codex platform daily",
                        PercentUsed = percent,
                        ResetIso = DateTime.UtcNow.Date.AddDays(1),
                    });
                }
            }
        }
        catch
        {
            // Body parse failure falls through to the no-buckets branch.
        }

        if (snap.Buckets.Count == 0)
        {
            snap.Ok = false;
            snap.Error = "no-platform-buckets";
        }
        return snap;
    }

    /// <summary>
    /// Legacy WHAM JSON parser kept around for testing — the
    /// chatgpt.com/backend-api/wham/usage shape. v0.5 doesn't call this
    /// endpoint (cookie-auth only); kept so the existing Pass 3 tests
    /// still pass and so a future Chromium-cookie path can reuse it.
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
        percent = v <= 1.0 ? v * 100 : v;
        return true;
    }

    public static DateTimeOffset? ParseEpochOrIso(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var epoch))
        {
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

    private sealed record OAuthCache(string AccessToken, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Token kind classification. Drives endpoint dispatch in
/// <see cref="CredentialPoller"/>.
/// </summary>
public enum TokenKind
{
    Unknown,
    OAuthBearer,
    AnthropicApiKey,
    AnthropicAdminKey,
    OpenAiApiKey,
}

/// <summary>
/// Pure data extracted from a credentials JSON. Provides everything
/// <see cref="CredentialPoller"/> needs to issue a probe request.
/// </summary>
public sealed record CredentialFile(string AccessToken, string? RefreshToken, string? OrgId, string? AccountId);
