namespace QuotaGlass.Shared;

/// <summary>
/// Pins the wire schema version range the current build understands.
/// See docs/extension-integration.md for the versioning policy.
/// </summary>
public static class SchemaVersion
{
    /// <summary>
    /// Wire schema version. v3 (R4-N5 / R3-P2-01) adds optional
    /// <see cref="ProviderMap.ClaudeAccounts"/> + <see cref="ProviderMap.CodexAccounts"/>
    /// so a single snapshot can carry multiple accounts per provider. v2
    /// (R4-N3) added <see cref="ExtensionState.History"/>. v1 was the
    /// original single-account / no-history shape. All bumps are additive;
    /// receivers running an older schema ignore the new fields.
    /// </summary>
    public const int Current = 3;
    public const int Min = 1;
    public const int Max = 3;

    public static bool IsSupported(int version) => version >= Min && version <= Max;
}
