using QuotaGlass.Widget.Services;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class ToastActionArgumentsTests
{
    [Fact]
    public void Build_and_parse_roundtrip_delimiters_inside_values()
    {
        var args = ToastActionArguments.Build(
            ("action", "snooze"),
            ("bucket", "acct=work;session"),
            ("duration", "PT1H"));

        Assert.Contains("bucket=acct%3Dwork%3Bsession", args);
        var parsed = ToastActionArguments.Parse(args);
        Assert.Equal("snooze", parsed["action"]);
        Assert.Equal("acct=work;session", parsed["bucket"]);
        Assert.Equal("PT1H", parsed["duration"]);
    }

    [Fact]
    public void Parse_keeps_legacy_unescaped_values_working()
    {
        var parsed = ToastActionArguments.Parse("action=snooze;bucket=plain-bucket;duration=PT1H");

        Assert.Equal("plain-bucket", parsed["bucket"]);
    }
}
