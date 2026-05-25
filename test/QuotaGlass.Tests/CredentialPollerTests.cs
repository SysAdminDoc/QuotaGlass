using System.Net;
using System.Net.Http;
using QuotaGlass.NMH;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// Covers the pure-functional pieces of F-N1 credential reading:
/// access-token extraction across the schema shapes seen in the wild,
/// header parsing into bucket percentages, and Codex WHAM JSON parsing.
/// The actual HttpClient round-trip lives behind an integration boundary
/// and is exercised end-to-end on the desktop PC.
/// </summary>
public sealed class CredentialPollerTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"qg-creds-tests-{Guid.NewGuid():N}");

    public CredentialPollerTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("""{"access_token":"sk-ant-oat01-xxx"}""", "sk-ant-oat01-xxx")]
    [InlineData("""{"accessToken":"abc"}""", "abc")]
    [InlineData("""{"token":"xyz"}""", "xyz")]
    [InlineData("""{"credentials":{"access_token":"nested"}}""", "nested")]
    [InlineData("""{"tokens":{"accessToken":"deep"}}""", "deep")]
    [InlineData("""{"auth":{"api_key":"ak"}}""", "ak")]
    [InlineData("""{"unrelated":"value"}""", null)]
    [InlineData("not json", null)]
    public void ExtractAccessToken_recognizes_schema_shapes(string payload, string? expected)
    {
        var path = Path.Combine(_tmpDir, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, payload);
        Assert.Equal(expected, CredentialPoller.ExtractAccessToken(path));
    }

    [Theory]
    [InlineData("0.42", true, 42.0)]
    [InlineData("1.0", true, 100.0)]
    [InlineData("0", true, 0.0)]
    [InlineData("42", true, 42.0)] // already in 0..100 scale
    [InlineData(null, false, 0.0)]
    [InlineData("garbage", false, 0.0)]
    public void TryParseRatio_normalizes_to_percent(string? raw, bool expectedOk, double expected)
    {
        var ok = CredentialPoller.TryParseRatio(raw, out var got);
        Assert.Equal(expectedOk, ok);
        if (ok) Assert.Equal(expected, got, precision: 5);
    }

    [Fact]
    public void ParseEpochOrIso_handles_iso_seconds_and_milliseconds()
    {
        var iso = CredentialPoller.ParseEpochOrIso("2026-06-01T12:00:00Z");
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), iso);

        var seconds = CredentialPoller.ParseEpochOrIso("1780000000");
        Assert.NotNull(seconds);
        Assert.Equal(2026, seconds!.Value.Year);

        var millis = CredentialPoller.ParseEpochOrIso("1780000000000");
        Assert.NotNull(millis);
        Assert.Equal(2026, millis!.Value.Year);

        Assert.Null(CredentialPoller.ParseEpochOrIso(null));
        Assert.Null(CredentialPoller.ParseEpochOrIso(string.Empty));
    }

    [Fact]
    public void ExtractClaudeBuckets_handles_both_5h_and_7d_headers()
    {
        using var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.Add("anthropic-ratelimit-unified-5h-utilization", "0.64");
        resp.Headers.Add("anthropic-ratelimit-unified-5h-reset", "2026-06-01T17:00:00Z");
        resp.Headers.Add("anthropic-ratelimit-unified-7d-utilization", "0.87");
        resp.Headers.Add("anthropic-ratelimit-unified-7d-reset", "2026-06-08T12:00:00Z");
        resp.Content = new StringContent("{}");

        var snap = CredentialPoller.ExtractClaudeBuckets(resp.Headers, resp.Content.Headers);

        Assert.True(snap.Ok);
        Assert.Equal(2, snap.Buckets.Count);
        Assert.Equal("claude-session", snap.Buckets[0].Id);
        Assert.Equal(64.0, snap.Buckets[0].PercentUsed, precision: 5);
        Assert.Equal("claude-weekly-all", snap.Buckets[1].Id);
        Assert.Equal(87.0, snap.Buckets[1].PercentUsed, precision: 5);
    }

    [Fact]
    public void ExtractClaudeBuckets_returns_error_when_headers_missing()
    {
        using var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent("{}");

        var snap = CredentialPoller.ExtractClaudeBuckets(resp.Headers, resp.Content.Headers);

        Assert.False(snap.Ok);
        Assert.Equal("no-rate-limit-headers", snap.Error);
    }

    [Fact]
    public void ExtractCodexBuckets_parses_wham_shape()
    {
        const string body = """
        {
            "primary_window": {
                "used_percent": 0.23,
                "resets_at": "2026-06-01T18:00:00Z"
            },
            "secondary_window": {
                "utilization": 0.91,
                "resets_at": "2026-06-08T18:00:00Z"
            }
        }
        """;
        using var resp = new HttpResponseMessage(HttpStatusCode.OK);

        var snap = CredentialPoller.ExtractCodexBuckets(body, resp.Headers);

        Assert.True(snap.Ok);
        Assert.Equal(2, snap.Buckets.Count);
        Assert.Equal("codex-5h-all", snap.Buckets[0].Id);
        Assert.Equal(23.0, snap.Buckets[0].PercentUsed, precision: 5);
        Assert.Equal("codex-weekly-all", snap.Buckets[1].Id);
        Assert.Equal(91.0, snap.Buckets[1].PercentUsed, precision: 5);
    }
}
