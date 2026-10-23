using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

/// <summary>debug_status with detail answers "where am I and what is going on" in one call.</summary>
public class StatusDetailTests
{
    private static FakeDebugBridge Stopped() => new() { Mode = DebuggerMode.Break };

    private static Task<ModelContextProtocol.Protocol.CallToolResult> Status(FakeConnectionManager cm, bool detail) =>
        new SessionTools(cm).Status(detail, CancellationToken.None);

    [Fact]
    public async Task Without_detail_only_the_status_is_read()
    {
        var cm = new FakeConnectionManager(Stopped());

        var json = Json(await Status(cm, detail: false));

        Assert.Equal(["GetStatus"], cm.Bridge.Calls);
        Assert.False(json.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Detail_in_break_mode_adds_the_stack_the_locals_and_the_exception()
    {
        var bridge = Stopped();
        bridge.OnException = () => new ExceptionInfo { Type = "System.InvalidOperationException", Message = "boom" };
        bridge.OnVariables = (_, _, _) => [new VariableInfo { Name = "order", Type = "Order", Value = "{Order}", HasChildren = true }];
        var cm = new FakeConnectionManager(bridge);

        var detail = Json(await Status(cm, detail: true)).GetProperty("detail");

        Assert.Equal("A.M", detail.GetProperty("stack")[0].GetProperty("function").GetString());
        Assert.Equal("order", detail.GetProperty("locals")[0].GetProperty("name").GetString());
        Assert.Equal("boom", detail.GetProperty("exception").GetProperty("message").GetString());
        Assert.Equal(["GetStatus", "GetThreadStack:", "GetVariables", "GetException"], cm.Bridge.Calls);
    }

    [Fact]
    public async Task Detail_leaves_the_exception_out_when_the_break_is_not_about_one()
    {
        var cm = new FakeConnectionManager(Stopped());

        var detail = Json(await Status(cm, detail: true)).GetProperty("detail");

        Assert.False(detail.TryGetProperty("exception", out _));
    }

    [Theory]
    [InlineData(DebuggerMode.Design)]
    [InlineData(DebuggerMode.Run)]
    public async Task Detail_outside_break_mode_reads_nothing_more(DebuggerMode mode)
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge { Mode = mode }, mode);

        var json = Json(await Status(cm, detail: true));

        Assert.Equal(["GetStatus"], cm.Bridge.Calls);
        Assert.False(json.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task A_part_that_cannot_be_read_is_named_and_the_rest_still_comes()
    {
        var bridge = Stopped();
        bridge.OnVariables = (_, _, _) => throw new McpBridgeException(BridgeErrorCode.EvalFailed, "no frame");
        var cm = new FakeConnectionManager(bridge);

        var json = Json(await Status(cm, detail: true));

        Assert.Equal("break", json.GetProperty("mode").GetString());
        var detail = json.GetProperty("detail");
        Assert.True(detail.TryGetProperty("stack", out _));
        Assert.False(detail.TryGetProperty("locals", out _));
        Assert.Contains("locals", detail.GetProperty("notes")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denied_part_is_not_swallowed()
    {
        var bridge = Stopped();
        bridge.OnVariables = (_, _, _) => throw new McpBridgeException(BridgeErrorCode.Denied, "no");
        var cm = new FakeConnectionManager(bridge);

        var json = Json(await Status(cm, detail: true));

        Assert.Equal("denied", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Many_locals_are_cut_and_say_so()
    {
        var bridge = Stopped();
        bridge.OnVariables = (_, _, max) => Enumerable.Range(0, max).Select(i => new VariableInfo { Name = "v" + i, Type = "int", Value = "1" }).ToArray();
        var cm = new FakeConnectionManager(bridge);

        var detail = Json(await Status(cm, detail: true)).GetProperty("detail");

        Assert.Equal(SessionTools.DetailLocals, detail.GetProperty("locals").GetArrayLength());
        Assert.True(detail.GetProperty("localsHasMore").GetBoolean());
    }

    [Fact]
    public async Task An_extension_without_the_thread_tools_gives_the_plain_stack_and_no_exception()
    {
        var cm = new FakeConnectionManager(Stopped()) { RemoteProtocolMinor = ProtocolInfo.MinorM3 - 1 };

        var detail = Json(await Status(cm, detail: true)).GetProperty("detail");

        Assert.True(detail.TryGetProperty("stack", out _));
        Assert.Contains("GetStack", cm.Bridge.Calls);
        Assert.DoesNotContain("GetException", cm.Bridge.Calls);
    }
}
