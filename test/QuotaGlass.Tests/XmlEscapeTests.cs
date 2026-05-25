using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// R4-N7 — locks in the XML escape behavior used by
/// <c>QuotaGlass.Widget.Services.ToastService</c>. Bucket labels come from
/// Anthropic / OpenAI API responses; a future migration to attribute-bound
/// values must not introduce an unescaped <c>'</c> or <c>"</c>.
/// </summary>
public sealed class XmlEscapeTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("safe text", "safe text")]
    [InlineData("&", "&amp;")]
    [InlineData("<", "&lt;")]
    [InlineData(">", "&gt;")]
    [InlineData("\"", "&quot;")]
    [InlineData("'", "&apos;")]
    [InlineData("Claude & Codex", "Claude &amp; Codex")]
    [InlineData("<script>alert('x')</script>",
        "&lt;script&gt;alert(&apos;x&apos;)&lt;/script&gt;")]
    public void Escape_handles_all_five_xml_entities(string? input, string expected)
    {
        Assert.Equal(expected, XmlEscape.Escape(input));
    }

    [Fact]
    public void Escape_is_idempotent_on_already_escaped_input()
    {
        var once = XmlEscape.Escape("a&b");
        // Already-encoded entities are re-encoded — this is OK; the
        // assumption is callers pass raw user input, not pre-encoded XML.
        Assert.Equal("a&amp;b", once);
        var twice = XmlEscape.Escape(once);
        Assert.Equal("a&amp;amp;b", twice);
    }

    [Fact]
    public void Escape_preserves_multibyte_characters()
    {
        // Non-ASCII text passes through unchanged.
        Assert.Equal("résumé · 日本語", XmlEscape.Escape("résumé · 日本語"));
    }

    [Fact]
    public void Escape_handles_mixed_content_in_one_pass()
    {
        var input = "5h \"limit\" at <90%>: \"don't\" & friends";
        var expected = "5h &quot;limit&quot; at &lt;90%&gt;: &quot;don&apos;t&quot; &amp; friends";
        Assert.Equal(expected, XmlEscape.Escape(input));
    }
}
