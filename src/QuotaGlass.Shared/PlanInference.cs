namespace QuotaGlass.Shared;

/// <summary>
/// L-07 — heuristic plan guesser from reset cadence + bucket kinds. The
/// extension already populates <see cref="ProviderSnapshot.Plan"/> when it
/// knows; this fills the gap when the field is empty / "unknown".
///
/// Used by the widget when displaying provider headers.
/// </summary>
public static class PlanInference
{
    /// <summary>
    /// Returns a best-effort plan label or <c>null</c> when we cannot
    /// distinguish. Heuristics (Claude):
    ///   - weekly bucket present with model-expanded variants → max-20x or max-5x
    ///   - weekly bucket present without model expansion → pro
    ///   - 5h bucket only → free
    /// (Codex):
    ///   - weekly bucket present → plus or team
    ///   - 5h bucket only → free
    /// </summary>
    public static string? Infer(ProviderSnapshot? provider)
    {
        if (provider is null || provider.Buckets.Count == 0) return provider?.Plan;
        if (!string.IsNullOrEmpty(provider.Plan) && provider.Plan != "unknown") return provider.Plan;

        var hasWeekly = provider.Buckets.Any(b => string.Equals(b.Kind, "weekly", StringComparison.OrdinalIgnoreCase));
        var weeklyModels = provider.Buckets
            .Where(b => string.Equals(b.Kind, "weekly", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Model)
            .Where(m => !string.IsNullOrEmpty(m) && m != "all")
            .Distinct()
            .Count();
        var hasSession = provider.Buckets.Any(b =>
            string.Equals(b.Kind, "session", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Kind, "5h", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(provider.Provider, "claude", StringComparison.OrdinalIgnoreCase))
        {
            if (weeklyModels >= 2) return "max-20x";
            if (weeklyModels == 1) return "max-5x";
            if (hasWeekly) return "pro";
            if (hasSession) return "free";
        }
        else if (string.Equals(provider.Provider, "codex", StringComparison.OrdinalIgnoreCase))
        {
            if (hasWeekly) return "plus";
            if (hasSession) return "free";
        }

        return null;
    }
}
