using VsDebug.Mcp.Tests.Fakes;
using VsDebug.Mcp.Tools;
using VsDebug.Protocol;
using Xunit;
using static VsDebug.Mcp.Tests.ExecutionToolsTests;

namespace VsDebug.Mcp.Tests;

public class BreakpointToolsTests
{
    [Fact]
    public async Task Requires_either_file_and_line_or_function()
    {
        var tools = new BreakpointTools(new FakeConnectionManager(new FakeDebugBridge()));
        var result = await tools.Set(file: "a.cs", line: null, function: null, condition: null, conditionKind: "true", hitCount: 0, hitKind: "none", logMessage: null, CancellationToken.None);
        Assert.Equal("invalid_argument", Json(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Maps_condition_and_hit_count()
    {
        var bridge = new FakeDebugBridge();
        BreakpointSpec? seen = null;
        bridge.OnSetBreakpoint = s => { seen = s; return new SetBreakpointResult { Breakpoint = new BreakpointInfo { Id = "bp-1" } }; };
        var tools = new BreakpointTools(new FakeConnectionManager(bridge));

        await tools.Set("OrderService.cs", 9, null, "total > 1000", "changed", 3, "atLeast", null, CancellationToken.None);

        Assert.Equal("total > 1000", seen!.Condition);
        Assert.Equal(ConditionKind.WhenChanged, seen.ConditionKind);
        Assert.Equal(HitCountKind.AtLeast, seen.HitCountKind);
        Assert.Equal(3, seen.HitCount);
    }

    [Fact]
    public async Task Remove_requires_id_or_all()
    {
        var tools = new BreakpointTools(new FakeConnectionManager(new FakeDebugBridge()));
        var result = await tools.Remove(id: null, all: false, CancellationToken.None);
        Assert.Equal("invalid_argument", Json(result).GetProperty("error").GetProperty("code").GetString());
    }
}
