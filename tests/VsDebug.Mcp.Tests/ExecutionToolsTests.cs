using System.Text.Json;
using ModelContextProtocol.Protocol;
using VsDebug.Mcp.Connection;
using VsDebug.Mcp.Tests.Fakes;
using VsDebug.Mcp.Tools;
using VsDebug.Protocol;
using Xunit;

namespace VsDebug.Mcp.Tests;

public class ExecutionToolsTests
{
    internal static JsonElement Json(CallToolResult r) => JsonDocument.Parse(((TextContentBlock)r.Content[0]).Text).RootElement;

    private static BreakEvent Break(long seq) => new()
    {
        Seq = seq,
        Snapshot = new BreakSnapshot
        {
            Reason = BreakReason.Step,
            Location = new SourceLocation { File = "OrderService.cs", Line = 9, Function = "OrderService.Submit" },
        },
    };

    [Fact]
    public async Task Step_returns_snapshot_of_next_break()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Bridge.OnStep = kind => { _ = Task.Run(() => cm.Session.OnBreakAsync(Break(11))); return 10; };
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var result = await tools.Step("over", CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var json = Json(result);
        Assert.Equal("break", json.GetProperty("state").GetString());
        Assert.Equal(9, json.GetProperty("snapshot").GetProperty("location").GetProperty("line").GetInt32());
        Assert.Contains("Step:Over", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Step_while_running_is_wrong_mode_with_hint()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Run);
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var result = await tools.Step("over", CancellationToken.None);

        Assert.True(result.IsError);
        var error = Json(result).GetProperty("error");
        Assert.Equal("wrong_mode", error.GetProperty("code").GetString());
        Assert.Contains("debug_pause", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("Step", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Continue_times_out_to_running_state()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Continue(CancellationToken.None));

        Assert.Equal("running", json.GetProperty("state").GetString());
        Assert.Contains("debug_wait", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Continue_returns_ended_when_session_ends()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Bridge.OnContinue = () =>
        {
            _ = Task.Run(() => cm.Session.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 11 }));
            return 10;
        };
        var tools = new ExecutionTools(cm, TimeSpan.FromSeconds(30));

        var started = DateTime.UtcNow;
        var json = Json(await tools.Continue(CancellationToken.None));

        Assert.Equal("design", json.GetProperty("state").GetString());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Invalid_step_kind_is_invalid_argument()
    {
        var tools = new ExecutionTools(new FakeConnectionManager(new FakeDebugBridge()), ExecutionTools.DefaultWait);

        var result = await tools.Step("sideways", CancellationToken.None);

        Assert.Equal("invalid_argument", Json(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Wait_caps_timeout_at_120_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), ExecutionTools.ClampWait(999));
        Assert.Equal(TimeSpan.FromSeconds(1), ExecutionTools.ClampWait(0));
    }

    [Fact]
    public async Task Bridge_failure_is_reported_as_error_code()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge())
        {
            FailWith = new McpBridgeException(BridgeErrorCode.NotConnected, "gone", "run doctor"),
        };
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var error = Json(await tools.Step("over", CancellationToken.None)).GetProperty("error");

        Assert.Equal("not_connected", error.GetProperty("code").GetString());
        Assert.Equal("run doctor", error.GetProperty("hint").GetString());
    }
}
