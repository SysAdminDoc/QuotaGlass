namespace QuotaGlass.Shared;

/// <summary>
/// Pins the wire schema version range the current build understands.
/// See docs/extension-integration.md for the versioning policy.
/// </summary>
public static class SchemaVersion
{
    public const int Current = 1;
    public const int Min = 1;
    public const int Max = 1;

    public static bool IsSupported(int version) => version >= Min && version <= Max;
}
