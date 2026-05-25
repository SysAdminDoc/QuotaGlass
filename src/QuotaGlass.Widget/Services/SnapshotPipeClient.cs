using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// L-06 / R4-N6 — best-effort pipe consumer. Connects to the NMH's
/// <see cref="SnapshotPipe.PipeName"/> server, reads framed JSON
/// snapshots, marshals them onto the dispatcher and raises
/// <see cref="SnapshotReceived"/>.
///
/// The widget keeps the FileSystemWatcher (SnapshotWatcher) running in
/// parallel — the pipe is purely a latency optimization. If the NMH
/// crashes / the user runs in --inject-fake-snapshot mode / Windows
/// teardown breaks the pipe, the watcher still picks up snapshot.json
/// writes.
///
/// Reconnect: on disconnect we wait 2 s then retry indefinitely until
/// disposed. Designed to survive NMH spawns-per-extension-connection.
/// </summary>
public sealed class SnapshotPipeClient : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public event EventHandler<SnapshotMessage>? SnapshotReceived;

    public SnapshotPipeClient(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    SnapshotPipe.PipeName,
                    PipeDirection.In,
                    PipeOptions.Asynchronous);

                await client.ConnectAsync(ct).ConfigureAwait(false);
                WidgetLogger.Info("Snapshot pipe: connected to NMH");
                await PumpAsync(client, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                WidgetLogger.Warn($"Snapshot pipe loop error: {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PumpAsync(NamedPipeClientStream client, CancellationToken ct)
    {
        var header = new byte[4];
        while (!ct.IsCancellationRequested && client.IsConnected)
        {
            var read = 0;
            while (read < 4)
            {
                var n = await client.ReadAsync(header.AsMemory(read, 4 - read), ct).ConfigureAwait(false);
                if (n == 0) return; // EOF — server disconnected
                read += n;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is <= 0 or > SnapshotPipe.MaxFrameBytes) return;

            var payload = new byte[length];
            var got = 0;
            while (got < length)
            {
                var n = await client.ReadAsync(payload.AsMemory(got, length - got), ct).ConfigureAwait(false);
                if (n == 0) return;
                got += n;
            }

            SnapshotMessage? message;
            try
            {
                message = JsonSerializer.Deserialize(payload, SnapshotJsonContext.Default.SnapshotMessage);
            }
            catch (JsonException)
            {
                continue;
            }
            if (message is null) continue;
            if (!SchemaVersion.IsSupported(message.SchemaVersion)) continue;

            _dispatcher.BeginInvoke(() => SnapshotReceived?.Invoke(this, message));
        }
    }
}
