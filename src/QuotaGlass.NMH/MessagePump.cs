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

    public MessagePump(string callerOrigin)
    {
        _callerOrigin = callerOrigin;
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
        BucketSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(payload, SnapshotJsonContext.Default.BucketSnapshot);
        }
        catch (JsonException ex)
        {
            Logger.Warn($"json decode failed: {ex.Message}");
            await WriteAckAsync(stdout, ok: false, "json-decode-failed").ConfigureAwait(false);
            return;
        }

        if (snapshot is null)
        {
            Logger.Warn("decoded snapshot was null");
            await WriteAckAsync(stdout, ok: false, "null-snapshot").ConfigureAwait(false);
            return;
        }

        if (!string.Equals(snapshot.Kind, "snapshot", StringComparison.Ordinal))
        {
            Logger.Warn($"unknown message kind: {snapshot.Kind}");
            await WriteAckAsync(stdout, ok: false, "unknown-kind").ConfigureAwait(false);
            return;
        }

        try
        {
            AtomicJsonFile.Write(AppPaths.SnapshotFile, snapshot, SnapshotJsonContext.Default.BucketSnapshot);
            Logger.Info($"persisted snapshot with {snapshot.Buckets.Count} buckets, source caller={_callerOrigin}");
            await WriteAckAsync(stdout, ok: true, "ok").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("snapshot write failed", ex);
            await WriteAckAsync(stdout, ok: false, "write-failed").ConfigureAwait(false);
        }
    }

    private static async Task WriteAckAsync(Stream stdout, bool ok, string detail)
    {
        var json = $"{{\"ok\":{(ok ? "true" : "false")},\"detail\":\"{detail}\"}}";
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
