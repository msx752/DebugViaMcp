using Xunit;
using DebugViaMcp.Vsix.Core.Instances;
using DebugViaMcp.Vsix.Core.Security;

namespace DebugViaMcp.Vsix.Core.Tests.Instances;

public class LeaseRulesTests
{
    [Fact]
    public void Every_operation_has_a_rule()
    {
        foreach (var operation in Enum.GetValues<Operation>())
            LeaseRules.For(operation);
    }

    [Fact]
    public void Rules_follow_the_tiers()
    {
        foreach (var operation in Enum.GetValues<Operation>())
        {
            var need = LeaseRules.For(operation);
            if (operation is Operation.Connect or Operation.Release)
            {
                Assert.Equal(LeaseNeed.None, need);
                continue;
            }

            var tier = PermissionPolicy.TierOf(operation);
            if (tier is PermissionTier.Control or PermissionTier.Mutate)
                Assert.Equal(LeaseNeed.Change, need);
            else if (operation == Operation.FrameSelect)
                Assert.Equal(LeaseNeed.Change, need);   // read tier, but it moves Visual Studio's current frame
            else
                Assert.Equal(LeaseNeed.Read, need);
        }
    }

    [Theory]
    [InlineData(Operation.Start)]
    [InlineData(Operation.Stop)]
    [InlineData(Operation.Build)]
    [InlineData(Operation.Attach)]
    [InlineData(Operation.SetBreakpoint)]
    [InlineData(Operation.SetValue)]
    [InlineData(Operation.EvaluateWithSideEffects)]
    [InlineData(Operation.FrameSelect)]
    public void State_changing_operations_need_the_lease(Operation operation) =>
        Assert.Equal(LeaseNeed.Change, LeaseRules.For(operation));

    [Theory]
    [InlineData(Operation.Status)]
    [InlineData(Operation.ListBreakpoints)]
    [InlineData(Operation.Stack)]
    [InlineData(Operation.Evaluate)]
    [InlineData(Operation.ListProcesses)]
    [InlineData(Operation.BuildCheck)]
    public void Reads_only_need_the_lease_to_be_free_or_theirs(Operation operation) =>
        Assert.Equal(LeaseNeed.Read, LeaseRules.For(operation));
}
