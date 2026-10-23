using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Security;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Security;

public class PermissionPolicyTests
{
    [Fact]
    public void Defaults_allow_read_and_control_and_ask_for_mutate()
    {
        var p = new PermissionSet();
        Assert.Equal(PermissionLevel.Allow, p.Read);
        Assert.Equal(PermissionLevel.Allow, p.Control);
        Assert.Equal(PermissionLevel.Ask, p.Mutate);
    }

    [Theory]
    [InlineData(Operation.Status, PermissionTier.Read)]
    [InlineData(Operation.ListBreakpoints, PermissionTier.Read)]
    [InlineData(Operation.Stack, PermissionTier.Read)]
    [InlineData(Operation.Variables, PermissionTier.Read)]
    [InlineData(Operation.Evaluate, PermissionTier.Read)]
    [InlineData(Operation.SetBreakpoint, PermissionTier.Control)]
    [InlineData(Operation.RemoveBreakpoints, PermissionTier.Control)]
    [InlineData(Operation.Start, PermissionTier.Control)]
    [InlineData(Operation.Stop, PermissionTier.Control)]
    [InlineData(Operation.Continue, PermissionTier.Control)]
    [InlineData(Operation.Pause, PermissionTier.Control)]
    [InlineData(Operation.Step, PermissionTier.Control)]
    [InlineData(Operation.EvaluateWithSideEffects, PermissionTier.Mutate)]
    [InlineData(Operation.SetValue, PermissionTier.Mutate)]
    [InlineData(Operation.SetBreakpointWithExpression, PermissionTier.Mutate)]
    [InlineData(Operation.Threads, PermissionTier.Read)]
    [InlineData(Operation.FrameSelect, PermissionTier.Read)]
    [InlineData(Operation.Exception, PermissionTier.Read)]
    [InlineData(Operation.EnableBreakpoint, PermissionTier.Control)]
    [InlineData(Operation.EnableBreakpointWithExpression, PermissionTier.Mutate)]
    [InlineData(Operation.RunTo, PermissionTier.Control)]
    [InlineData(Operation.ListProcesses, PermissionTier.Read)]
    [InlineData(Operation.SymbolStatus, PermissionTier.Read)]
    [InlineData(Operation.ListExceptionSettings, PermissionTier.Read)]
    [InlineData(Operation.SetExceptionSettings, PermissionTier.Control)]
    [InlineData(Operation.ResetExceptionSettings, PermissionTier.Control)]
    [InlineData(Operation.Attach, PermissionTier.Mutate)]
    [InlineData(Operation.OutputRead, PermissionTier.Read)]
    [InlineData(Operation.ErrorList, PermissionTier.Read)]
    [InlineData(Operation.Build, PermissionTier.Control)]
    [InlineData(Operation.Connect, PermissionTier.Read)]
    [InlineData(Operation.Release, PermissionTier.Read)]
    [InlineData(Operation.BuildCheck, PermissionTier.Read)]
    public void Every_operation_has_its_spec_tier(Operation operation, PermissionTier tier) =>
        Assert.Equal(tier, PermissionPolicy.TierOf(operation));

    [Fact]
    public void Every_operation_is_covered_by_the_table()
    {
        foreach (var op in Enum.GetValues<Operation>())
        {
            PermissionPolicy.TierOf(op);
            Assert.False(string.IsNullOrEmpty(PermissionPolicy.ToolName(op)));
        }
    }

    [Fact]
    public void Decide_reads_the_level_of_the_operations_tier()
    {
        var set = new PermissionSet { Read = PermissionLevel.Deny, Control = PermissionLevel.Ask, Mutate = PermissionLevel.Allow };
        Assert.Equal(PermissionLevel.Deny, PermissionPolicy.Decide(Operation.Stack, set));
        Assert.Equal(PermissionLevel.Ask, PermissionPolicy.Decide(Operation.Step, set));
        Assert.Equal(PermissionLevel.Allow, PermissionPolicy.Decide(Operation.SetValue, set));
    }

    [Fact]
    public void Tool_and_tier_names_match_the_mcp_contract()
    {
        Assert.Equal("set_value", PermissionPolicy.ToolName(Operation.SetValue));
        Assert.Equal("eval", PermissionPolicy.ToolName(Operation.EvaluateWithSideEffects));
        Assert.Equal("debug_step", PermissionPolicy.ToolName(Operation.Step));
        Assert.Equal("mutate", PermissionPolicy.TierName(PermissionTier.Mutate));
        Assert.Equal("connect", PermissionPolicy.ToolName(Operation.Connect));
        Assert.Equal("vs_release", PermissionPolicy.ToolName(Operation.Release));
        Assert.Equal("build_check", PermissionPolicy.ToolName(Operation.BuildCheck));
        Assert.Equal("exception_list", PermissionPolicy.ToolName(Operation.ListExceptionSettings));
        Assert.Equal("exception_set", PermissionPolicy.ToolName(Operation.SetExceptionSettings));
        Assert.Equal("exception_reset", PermissionPolicy.ToolName(Operation.ResetExceptionSettings));
    }
}
