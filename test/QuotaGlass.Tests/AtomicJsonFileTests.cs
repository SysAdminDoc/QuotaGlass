using System.Text.Json.Serialization;
using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class AtomicJsonFileTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"qg-tests-{Guid.NewGuid():N}");

    public AtomicJsonFileTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Fact]
    public void Roundtrip_returns_equivalent_object()
    {
        var path = Path.Combine(_tmpDir, "roundtrip.json");
        var msg = NewMinimalMessage();

        AtomicJsonFile.Write(path, msg, SnapshotJsonContext.Default.SnapshotMessage);
        var read = AtomicJsonFile.Read(path, SnapshotJsonContext.Default.SnapshotMessage);

        Assert.NotNull(read);
        Assert.Equal(msg.Kind, read!.Kind);
        Assert.Equal(msg.SchemaVersion, read.SchemaVersion);
        Assert.NotNull(read.State);
        Assert.NotNull(read.State!.Providers.Claude);
        Assert.Single(read.State.Providers.Claude!.Buckets);
        Assert.Equal("claude-session", read.State.Providers.Claude.Buckets[0].Id);
        Assert.Equal(42.0, read.State.Providers.Claude.Buckets[0].PercentUsed);
    }

    [Fact]
    public void Read_returns_null_on_missing_file()
    {
        var path = Path.Combine(_tmpDir, "missing.json");
        Assert.Null(AtomicJsonFile.Read(path, SnapshotJsonContext.Default.SnapshotMessage));
    }

    [Fact]
    public void Read_returns_null_on_invalid_json()
    {
        var path = Path.Combine(_tmpDir, "garbage.json");
        File.WriteAllText(path, "{not json");
        Assert.Null(AtomicJsonFile.Read(path, SnapshotJsonContext.Default.SnapshotMessage));
    }

    [Fact]
    public void Replace_does_not_leave_orphan_tmp_on_success()
    {
        var path = Path.Combine(_tmpDir, "replace.json");
        AtomicJsonFile.Write(path, NewMinimalMessage(), SnapshotJsonContext.Default.SnapshotMessage);
        AtomicJsonFile.Write(path, NewMinimalMessage(), SnapshotJsonContext.Default.SnapshotMessage);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    private static SnapshotMessage NewMinimalMessage() => new()
    {
        Kind = "snapshot",
        SchemaVersion = SchemaVersion.Current,
        Timestamp = DateTimeOffset.UtcNow,
        ExtensionVersion = "0.0.0-test",
        State = new ExtensionState
        {
            FetchedAtIso = DateTimeOffset.UtcNow,
            Providers = new ProviderMap
            {
                Claude = new ProviderSnapshot
                {
                    Ok = true,
                    Provider = "claude",
                    Source = "api",
                    Buckets = new()
                    {
                        new Bucket
                        {
                            Id = "claude-session",
                            Kind = "session",
                            Model = "all",
                            Label = "Test session",
                            PercentUsed = 42.0,
                            ResetIso = DateTimeOffset.UtcNow.AddHours(2),
                        },
                    },
                },
            },
        },
    };
}
