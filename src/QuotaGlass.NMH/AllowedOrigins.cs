namespace QuotaGlass.NMH;

/// <summary>
/// Single source of truth for which browser extensions are permitted to talk
/// to this NMH. Read by both <see cref="HostRegistrar"/> (writes them into
/// the per-browser manifest JSON) and <see cref="MessagePump"/> (rejects any
/// inbound frame whose caller origin is not on the list).
/// </summary>
internal static class AllowedOrigins
{
    // Placeholder AI-Usage_Tracker Chrome ID. Will be replaced with the
    // deterministic ID derived from the manifest "key" once F-A2 lands.
    public const string AiUsageTrackerChromeId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public const string AiUsageTrackerFirefoxId = "ai-usage-tracker@sysadmindoc.dev";

    public static readonly string[] ChromiumOrigins =
    {
        $"chrome-extension://{AiUsageTrackerChromeId}/",
    };

    public static readonly string[] FirefoxExtensionIds =
    {
        AiUsageTrackerFirefoxId,
    };

    public static bool IsAllowed(string callerOrigin)
    {
        if (string.IsNullOrWhiteSpace(callerOrigin)) return false;

        foreach (var origin in ChromiumOrigins)
        {
            if (string.Equals(origin, callerOrigin, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Firefox passes the bare extension ID, not a chrome-extension:// URL.
        foreach (var id in FirefoxExtensionIds)
        {
            if (string.Equals(id, callerOrigin, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
