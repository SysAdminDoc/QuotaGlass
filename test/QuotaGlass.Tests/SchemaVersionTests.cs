using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class SchemaVersionTests
{
    [Fact]
    public void Current_lies_within_supported_range()
    {
        Assert.InRange(SchemaVersion.Current, SchemaVersion.Min, SchemaVersion.Max);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(99, false)]
    public void IsSupported_handles_in_range_and_out_of_range(int version, bool expected)
    {
        Assert.Equal(expected, SchemaVersion.IsSupported(version));
    }
}
