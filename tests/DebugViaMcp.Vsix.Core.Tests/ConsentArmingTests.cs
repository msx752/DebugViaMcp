using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

/// <summary>Security review: a click that lands as the info bar appears must not count as consent.</summary>
public class ConsentArmingTests
{
    [Theory]
    [InlineData(ConsentChoice.AllowOnce)]
    [InlineData(ConsentChoice.AllowSession)]
    public void Allow_clicks_right_after_the_question_appears_are_ignored(ConsentChoice choice) =>
        Assert.False(ConsentArming.Accepts(choice, TimeSpan.FromMilliseconds(300)));

    [Fact]
    public void Allow_clicks_after_the_arming_delay_count() =>
        Assert.True(ConsentArming.Accepts(ConsentChoice.AllowOnce, ConsentArming.Delay));

    [Fact]
    public void Deny_always_counts() =>
        Assert.True(ConsentArming.Accepts(ConsentChoice.Deny, TimeSpan.Zero));
}
