using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// L-06 / R4-N6 — best-effort pipe publisher. <see cref="Broadcast"/> is
/// fire-and-forget; if no widget is connected (cold start, widget closed,
/// or process crash), the call is a no-op and the widget falls back to the
/// 250 ms FileSystemWatcher path.
///
/// We use <see cref="NamedPipeServerStream"/> in async-await mode with a
/// queue of pending writes so multiple snapshots coalesce naturally. The
/// pipe is per-instance — one widget consumer at a time. If we ever ship
/// multi-instance widgets, bump the maxNumberOfServerInstances arg.
/// </summary>
internal static class SnapshotPipeServer
{
    private static NamedPipeServerStream? _server;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private static volatile bool _connected;

    public static async Task StartAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _server?.Dispose();
                // R5-N5 / Pass 5 — restrict the pipe ACL to the current
                // user. Default ACL allows same-user spoofing; with the
                // explicit security descriptor below, only the SID that
                // owns the NMH process can read the pipe.
                _server = NamedPipeServerStreamAcl.Create(
                    SnapshotPipe.PipeName,
                    PipeDirection.Out,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: BuildCurrentUserOnlyPipeSecurity());

                try
                {
                    await _server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    _connected = true;
                    Logger.Info("Snapshot pipe: widget connected");

                    // Block until the client disconnects. We don't read
                    // anything from the widget side; the pipe is one-way
                    // (NMH → widget).
                    var probe = new byte[1];
                    try
                    {
                        while (_server.IsConnected && !ct.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { /* ct triggered */ }
                }
                catch (OperationCanceledException) { /* ct triggered */ }
                catch (IOException ex)
                {
                    Logger.Warn($"Snapshot pipe broken: {ex.Message}");
                }
                finally
                {
                    _connected = false;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Snapshot pipe server crashed", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static PipeSecurity BuildCurrentUserOnlyPipeSecurity()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user is null) return security;
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    public static async Task BroadcastAsync(SnapshotMessage message)
    {
        if (!_connected || _server is null) return;
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_server.IsConnected) { _connected = false; return; }
                var json = JsonSerializer.Serialize(message, SnapshotJsonContext.Default.SnapshotMessage);
                var bytes = Encoding.UTF8.GetBytes(json);
                if (bytes.Length > SnapshotPipe.MaxFrameBytes) return;
                var header = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
                await _server.WriteAsync(header).ConfigureAwait(false);
                await _server.WriteAsync(bytes).ConfigureAwait(false);
                await _server.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (IOException)
        {
            _connected = false;
        }
        catch (ObjectDisposedException)
        {
            _connected = false;
        }
    }
}
