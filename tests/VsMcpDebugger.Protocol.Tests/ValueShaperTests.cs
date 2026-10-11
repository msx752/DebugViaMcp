using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Protocol.Tests;

public class ValueShaperTests
{
    [Fact]
    public void Short_value_is_unchanged()
    {
        var (value, truncated) = ValueShaper.Truncate("abc");
        Assert.Equal("abc", value);
        Assert.False(truncated);
    }

    [Fact]
    public void Long_value_is_cut_at_max_with_ellipsis_marker()
    {
        var (value, truncated) = ValueShaper.Truncate(new string('x', 5000));
        Assert.True(truncated);
        Assert.Equal(ValueShaper.MaxValueLength, value.Length);
        Assert.EndsWith("…", value, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_becomes_empty()
    {
        var (value, truncated) = ValueShaper.Truncate(null);
        Assert.Equal("", value);
        Assert.False(truncated);
    }
}
