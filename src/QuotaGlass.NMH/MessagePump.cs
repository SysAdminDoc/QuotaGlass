using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// Reads native-messaging frames off stdin, decodes JSON snapshots, and
/// persists the latest snapshot to <see cref="AppPaths.SnapshotFile"/>.
/// Chrome's protocol: 4-byte little-endian length prefix + UTF-8 JSON payload.
/// Max 1 MB per inbound message.
/// </summary>
internal sealed class MessagePump
{
    private const int MaxInboundBytes = 1024 * 1024; // 1 MB per Chrome spec.

    private readonly string _callerOrigin;
    private readonly bool _originAllowed;

    public MessagePump(string callerOrigin)
    {
        _callerOrigin = callerOrigin;
        _originAllowed = AllowedOrigins.IsAllowed(callerOrigin);
        if (!_originAllowed)
        {
            Logger.Warn($"caller origin not on allow-list: {callerOrigin}");
        }
    }

    public async Task<int> RunAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        var lengthBuf = new byte[4];

        while (true)
        {
            var headerRead = await ReadExactAsync(stdin, lengthBuf, 0, 4).ConfigureAwait(false);
            if (headerRead == 0)
            {
                Logger.Info("stdin closed by browser, exiting");
                return 0;
            }
            if (headerRead != 4)
            {
                Logger.Warn($"short header: read {headerRead} bytes, expected 4");
                return 1;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);
            if (length is <= 0 or > MaxInboundBytes)
            {
                Logger.Warn($"invalid frame length: {length}");
                return 1;
            }

            var payload = new byte[length];
            var payloadRead = await ReadExactAsync(stdin, payload, 0, length).ConfigureAwait(false);
            if (payloadRead != length)
            {
                Logger.Warn($"short payload: read {payloadRead} bytes, expected {length}");
                return 1;
            }

            await HandleMessageAsync(payload, stdout).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(byte[] payload, Stream stdout)
    {
        if (!_originAllowed)
        {
            await WriteAckAsync(stdout, ok: false, "origin-rejected", kind: null).ConfigureAwait(false);
            return;
        }

        SnapshotMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(payload, SnapshotJsonContext.Default.SnapshotMessage);
        }
        catch (JsonException ex)
        {
            // System.Text.Json wraps MaxDepth violations as JsonException with
            // a specific message; surface as max-depth-exceeded so the bridge
            // can distinguish a malformed payload from a depth attack.
            var detail = ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase)
                ? "max-depth-exceeded"
                : "json-decode-failed";
            Logger.Warn($"json decode failed: {ex.Message}");
            await WriteAckAsync(stdout, ok: false, detail, kind: null).ConfigureAwait(false);
            return;
        }

        if (message is null)
        {
            Logger.Warn("decoded message was null");
            await WriteAckAsync(stdout, ok: false, "null-snapshot", kind: null).ConfigureAwait(false);
            return;
        }

        if (string.Equals(message.Kind, "ping", StringComparison.Ordinal))
        {
            // Keepalive: extension uses this to defeat Chrome MV3's 30s
            // service-worker idle timer.
            await WriteAckAsync(stdout, ok: true, "pong", kind: "pong").ConfigureAwait(false);
            return;
        }

        if (!string.Equals(message.Kind, "snapshot", StringComparison.Ordinal))
        {
            Logger.Warn($"unknown message kind: {message.Kind}");
            await WriteAckAsync(stdout, ok: false, "unknown-kind", kind: null).ConfigureAwait(false);
            return;
        }

        if (!SchemaVersion.IsSupported(message.SchemaVersion))
        {
            var detail = message.SchemaVersion < SchemaVersion.Min
                ? "schema-too-old"
                : "schema-too-new";
            Logger.Warn($"unsupported schema version: {message.SchemaVersion} (range {SchemaVersion.Min}..{SchemaVersion.Max})");
            await WriteAckAsync(stdout, ok: false, detail, kind: null).ConfigureAwait(false);
            return;
        }

        if (message.State is null)
        {
            Logger.Warn("snapshot kind frame had null state");
            await WriteAckAsync(stdout, ok: false, "null-snapshot", kind: null).ConfigureAwait(false);
            return;
        }

        try
        {
            AtomicJsonFile.Write(AppPaths.SnapshotFile, message, SnapshotJsonContext.Default.SnapshotMessage);
            var claudeCount = message.State.Providers.Claude?.Buckets?.Count ?? 0;
            var codexCount = message.State.Providers.Codex?.Buckets?.Count ?? 0;
            Logger.Info($"persisted snapshot — claude={claudeCount} codex={codexCount} caller={_callerOrigin}");
            await WriteAckAsync(stdout, ok: true, "ok", kind: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("snapshot write failed", ex);
            await WriteAckAsync(stdout, ok: false, "write-failed", kind: null).ConfigureAwait(false);
        }
    }

    private static async Task WriteAckAsync(Stream stdout, bool ok, string detail, string? kind)
    {
        var version = HostMetadata.Version;
        var ts = DateTimeOffset.UtcNow.ToString("O");
        var kindFragment = kind is null ? string.Empty : $"\"kind\":\"{kind}\",";
        // Forward-compat handshake — extension can read these to decide whether
        // to upgrade its payload shape. Schema range is the NMH-supported span.
        var json = $"{{\"ok\":{(ok ? "true" : "false")},{kindFragment}\"detail\":\"{detail}\","
                 + $"\"nmhVersion\":\"{version}\",\"schemaMin\":{HostMetadata.SchemaMin},"
                 + $"\"schemaMax\":{HostMetadata.SchemaMax},\"serverTime\":\"{ts}\"}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stdout.WriteAsync(header).ConfigureAwait(false);
        await stdout.WriteAsync(bytes).ConfigureAwait(false);
        await stdout.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total)).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
