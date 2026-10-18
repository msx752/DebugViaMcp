using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

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
    public async Task Bp_set_checks_the_file_scope_and_reports_warnings()
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => [new BuildWarning { Code = "stale_build", Message = "m", Project = "OrderDemo" }] };
        var tools = new BreakpointTools(new FakeConnectionManager(bridge));

        var json = Json(await tools.Set("src/x.cs", 3, null, null, "true", 0, "none", null, CancellationToken.None));

        Assert.Contains("CheckBuild:src/x.cs", bridge.Calls);
        Assert.Equal("stale_build", json.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Equal("bp-1", json.GetProperty("breakpoint").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Bp_set_on_a_function_has_no_file_to_check()
    {
        var bridge = new FakeDebugBridge();
        var tools = new BreakpointTools(new FakeConnectionManager(bridge));

        var json = Json(await tools.Set(null, null, "OrderDemo.OrderService.Submit", null, "true", 0, "none", null, CancellationToken.None));

        Assert.DoesNotContain(bridge.Calls, c => c.StartsWith("CheckBuild", StringComparison.Ordinal));
        Assert.False(json.TryGetProperty("warnings", out _));
    }

    [Fact]
    public async Task Bp_set_skips_the_check_against_a_2_1_extension_and_in_use_stops_the_set()
    {
        var old = new FakeDebugBridge();
        await new BreakpointTools(new FakeConnectionManager(old) { RemoteProtocolMinor = ProtocolInfo.MinorM3 })
            .Set("a.cs", 1, null, null, "true", 0, "none", null, CancellationToken.None);
        Assert.DoesNotContain(old.Calls, c => c.StartsWith("CheckBuild", StringComparison.Ordinal));

        var busy = new FakeDebugBridge { OnCheckBuild = (_, _) => throw new StreamJsonRpc.RemoteInvocationException("in use", (int)BridgeErrorCode.InUse, new BridgeErrorData()) };
        var json = Json(await new BreakpointTools(new FakeConnectionManager(busy)).Set("a.cs", 1, null, null, "true", 0, "none", null, CancellationToken.None));
        Assert.Equal("in_use", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("SetBreakpoint", busy.Calls);
    }

    [Fact]
    public async Task Remove_requires_id_or_all()
    {
        var tools = new BreakpointTools(new FakeConnectionManager(new FakeDebugBridge()));
        var result = await tools.Remove(id: null, all: false, CancellationToken.None);
        Assert.Equal("invalid_argument", Json(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Enable_returns_the_breakpoint_or_not_found()
    {
        var bridge = new FakeDebugBridge();
        var tools = new BreakpointTools(new FakeConnectionManager(bridge));

        var on = Json(await tools.Enable("bp-1", false, CancellationToken.None));
        bridge.OnEnable = (_, _) => null;
        var missing = Json(await tools.Enable("bp-2", true, CancellationToken.None));

        Assert.False(on.GetProperty("breakpoint").GetProperty("enabled").GetBoolean());
        Assert.True(missing.GetProperty("notFound").GetBoolean());
    }
}
