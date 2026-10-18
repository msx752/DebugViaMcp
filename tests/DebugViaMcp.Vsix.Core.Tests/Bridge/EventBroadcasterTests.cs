using Xunit;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Instances;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

public class EventBroadcasterTests
{
    [Fact]
    public void Without_a_holder_every_connection_receives_events() =>
        Assert.Equal([1, 2, 3], EventBroadcaster.SelectRecipients([1, 2, 3], null));

    [Fact]
    public void With_a_holder_only_the_holder_receives_them() =>
        Assert.Equal([2], EventBroadcaster.SelectRecipients([1, 2, 3], new LeaseHolder(2, "other-agent", null)));

    [Fact]
    public void A_holder_that_is_not_connected_receives_nothing_and_nobody_else_does() =>
        Assert.Empty(EventBroadcaster.SelectRecipients([1, 3], new LeaseHolder(2, "other-agent", null)));

    [Fact]
    public void Selection_keeps_the_input_order() =>
        Assert.Equal([3, 1], EventBroadcaster.SelectRecipients([3, 1], null));

    [Fact]
    public void No_connections_select_nobody() =>
        Assert.Empty(EventBroadcaster.SelectRecipients([], new LeaseHolder(1, "a", null)));
}
