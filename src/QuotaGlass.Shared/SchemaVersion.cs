namespace QuotaGlass.Shared;

/// <summary>
/// Pins the wire schema version range the current build understands.
/// See docs/extension-integration.md for the versioning policy.
/// </summary>
public static class SchemaVersion
{
    /// <summary>
    /// Wire schema version. v2 (R4-N3) adds optional <see cref="ExtensionState.History"/>
    /// so the extension can bundle 24-sample per-bucket history alongside the
    /// snapshot. Receivers that accept v1 continue to work — the new field is
    /// additive and ignored by older consumers.
    /// </summary>
    public const int Current = 2;
    public const int Min = 1;
    public const int Max = 2;

    public static bool IsSupported(int version) => version >= Min && version <= Max;
}
