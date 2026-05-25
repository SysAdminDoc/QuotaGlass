using System.Text.Json;
using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class SnapshotSchemaTests
{
    private const string ExtensionFixture = """
    {
      "kind": "snapshot",
      "schemaVersion": 1,
      "ts": "2026-05-25T01:23:45.678Z",
      "extensionVersion": "0.1.6",
      "state": {
        "fetchedAtISO": "2026-05-25T01:23:30.000Z",
        "providers": {
          "claude": {
            "ok": true,
            "provider": "claude",
            "source": "api",
            "orgId": "abc-123",
            "plan": "max-20x",
            "buckets": [
              {
                "id": "claude-session",
                "kind": "session",
                "model": "all",
                "label": "Claude 5h session",
                "percentUsed": 64.2,
                "resetISO": "2026-05-25T06:00:00.000Z",
                "rawResetText": "Resets 1:00 AM"
              }
            ]
          },
          "codex": {
            "ok": false,
            "provider": "codex",
            "source": "api",
            "error": "auth-token-not-found"
          }
        }
      }
    }
    """;

    [Fact]
    public void Deserialize_extension_fixture_lossless()
    {
        var msg = JsonSerializer.Deserialize(ExtensionFixture, SnapshotJsonContext.Default.SnapshotMessage);
        Assert.NotNull(msg);
        Assert.Equal("snapshot", msg!.Kind);
        Assert.Equal(1, msg.SchemaVersion);
        Assert.Equal("0.1.6", msg.ExtensionVersion);

        Assert.NotNull(msg.State);
        Assert.NotNull(msg.State!.Providers.Claude);
        Assert.True(msg.State.Providers.Claude!.Ok);
        Assert.Equal("max-20x", msg.State.Providers.Claude.Plan);
        Assert.Equal("abc-123", msg.State.Providers.Claude.OrgId);

        var bucket = msg.State.Providers.Claude.Buckets[0];
        Assert.Equal("claude-session", bucket.Id);
        Assert.Equal("session", bucket.Kind);
        Assert.Equal("all", bucket.Model);
        Assert.Equal(64.2, bucket.PercentUsed);
        Assert.NotNull(bucket.ResetIso);
        Assert.Equal("Resets 1:00 AM", bucket.RawResetText);

        Assert.NotNull(msg.State.Providers.Codex);
        Assert.False(msg.State.Providers.Codex!.Ok);
        Assert.Equal("auth-token-not-found", msg.State.Providers.Codex.Error);
    }

    [Fact]
    public void Deeply_nested_payload_rejected_via_MaxDepth()
    {
        // Build a JSON payload 20 arrays deep — exceeds the MaxDepth=16 in
        // SnapshotJsonContext (R2-P1-02). System.Text.Json throws JsonException.
        var nested = string.Concat(Enumerable.Repeat("[", 20)) + "1"
                     + string.Concat(Enumerable.Repeat("]", 20));
        var payload = "{\"kind\":\"snapshot\",\"schemaVersion\":1,\"deep\":" + nested + "}";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(payload, SnapshotJsonContext.Default.SnapshotMessage));
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        const string fixture = """{"kind":"snapshot","schemaVersion":1,"unknownTopLevel":"ok"}""";
        var msg = JsonSerializer.Deserialize(fixture, SnapshotJsonContext.Default.SnapshotMessage);
        Assert.NotNull(msg);
        Assert.Equal(1, msg!.SchemaVersion);
    }
}
