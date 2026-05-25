namespace QuotaGlass.Shared;

/// <summary>
/// L-06 / R4-N6 — named-pipe contract between QuotaGlass.NMH (server)
/// and QuotaGlass.Widget (client). Drops snapshot→render latency from
/// the ~270 ms FileSystemWatcher floor to <10 ms. Falls back gracefully
/// to FileSystemWatcher when no listener is connected.
///
/// Wire format mirrors the native messaging protocol: 4-byte little-
/// endian length prefix + UTF-8 JSON payload. Same envelope shape as
/// the on-disk snapshot.json so widget and NMH share parsers.
///
/// Pipe name lives in Shared so both projects reference one constant.
/// Per-user only — the pipe ACL is the default (current-user RW).
/// </summary>
public static class SnapshotPipe
{
    public const string PipeName = "QuotaGlass.Snapshot";
    public const int MaxFrameBytes = 1024 * 1024; // matches MessagePump cap
}
