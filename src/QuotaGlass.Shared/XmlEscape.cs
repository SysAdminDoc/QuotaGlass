namespace QuotaGlass.Shared;

/// <summary>
/// R4-N7 — minimal XML 1.0 character escaper used by the toast XML builder
/// in <c>QuotaGlass.Widget.Services.ToastService</c>. Lives in Shared so
/// the safety-critical escape logic can be unit-tested without a WPF
/// dependency.
///
/// Covers the five entities required for both element text and attribute
/// values: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&quot;</c>, <c>&apos;</c>.
/// XML 1.0 text nodes technically allow raw <c>&gt;</c> and raw <c>'</c>
/// in some positions, but emitting the entities everywhere is unambiguously
/// safe and matches what every standards-compliant parser expects.
/// </summary>
public static class XmlEscape
{
    public static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
