using QuotaGlass.Shared;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Dev mode: writes a deterministic snapshot.json so the widget can be
/// exercised in isolation without the full extension → NMH chain.
/// Activated via the <c>--inject-fake-snapshot</c> CLI flag.
/// </summary>
public static class FakeSnapshotInjector
{
    public static void Inject()
    {
        AppPaths.EnsureCreated();
        var now = DateTimeOffset.UtcNow;

        var msg = new SnapshotMessage
        {
            Kind = "snapshot",
            SchemaVersion = SchemaVersion.Current,
            Timestamp = now,
            ExtensionVersion = "fake-0.0.0",
            State = new ExtensionState
            {
                FetchedAtIso = now,
                Providers = new ProviderMap
                {
                    Claude = new ProviderSnapshot
                    {
                        Ok = true,
                        Provider = "claude",
                        Source = "api",
                        OrgId = "fake-org-uuid",
                        Plan = "max-20x",
                        Buckets = new()
                        {
                            new Bucket
                            {
                                Id = "claude-session",
                                Kind = "session",
                                Model = "all",
                                Label = "Claude 5h session",
                                PercentUsed = 64,
                                ResetIso = now.AddHours(2).AddMinutes(13),
                                RawResetText = null,
                            },
                            new Bucket
                            {
                                Id = "claude-weekly-all",
                                Kind = "weekly",
                                Model = "all",
                                Label = "Claude weekly",
                                PercentUsed = 87,
                                ResetIso = now.AddDays(3).AddHours(4),
                                RawResetText = null,
                            },
                        },
                    },
                    Codex = new ProviderSnapshot
                    {
                        Ok = true,
                        Provider = "codex",
                        Source = "api",
                        AccountId = "fake-account-id",
                        Plan = "plus",
                        Buckets = new()
                        {
                            new Bucket
                            {
                                Id = "codex-5h-all",
                                Kind = "5h",
                                Model = "all",
                                Label = "Codex 5-hour limit",
                                PercentUsed = 23,
                                ResetIso = now.AddHours(4).AddMinutes(45),
                                RawResetText = null,
                            },
                            new Bucket
                            {
                                Id = "codex-weekly-all",
                                Kind = "weekly",
                                Model = "all",
                                Label = "Codex weekly limit",
                                PercentUsed = 91,
                                ResetIso = now.AddDays(5).AddHours(2),
                                RawResetText = null,
                            },
                        },
                    },
                },
            },
        };

        AtomicJsonFile.Write(AppPaths.SnapshotFile, msg, SnapshotJsonContext.Default.SnapshotMessage);
    }
}
