using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Logging;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;
using Xunit;
using static VsMcpDebugger.Mcp.Tests.ExecutionToolsTests;

namespace VsMcpDebugger.Mcp.Tests;

public class SafetyToolsTests
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Set_value_returns_the_new_value_in_data()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await new InspectionTools(cm).SetValue("total", "0", CancellationToken.None));

        Assert.Equal("0", json.GetProperty("data").GetProperty("value").GetString());
        Assert.Contains("SetValue", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Set_value_requires_break_mode()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Run);
        var json = Json(await new InspectionTools(cm).SetValue("total", "0", CancellationToken.None));
        Assert.Equal("wrong_mode", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Set_value_rejected_by_visual_studio_is_eval_failed()
    {
        var bridge = new FakeDebugBridge { OnSetValue = (_, _) => new EvaluateResult { IsValid = false, Value = "Cannot convert" } };
        var json = Json(await new InspectionTools(new FakeConnectionManager(bridge)).SetValue("total", "\"x\"", CancellationToken.None));
        Assert.Equal("eval_failed", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Eval_forwards_allow_side_effects()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var tools = new InspectionTools(cm);

        await tools.Eval("order.Touch()", allowSideEffects: true, CancellationToken.None);
        Assert.True(cm.Bridge.LastAllowSideEffects);

        await tools.Eval("order.Touch()", ct: CancellationToken.None);
        Assert.False(cm.Bridge.LastAllowSideEffects);
    }

    [Fact]
    public async Task Status_reports_permissions()
    {
        var bridge = new FakeDebugBridge { OnStatus = () => new StatusInfo { Mode = DebuggerMode.Break, Permissions = new PermissionSet { Mutate = PermissionLevel.Deny } } };

        var json = Json(await new SessionTools(new FakeConnectionManager(bridge)).Status(CancellationToken.None));

        Assert.Equal("deny", json.GetProperty("permissions").GetProperty("mutate").GetString());
    }

    [Fact]
    public void Snapshot_args_are_returned_under_data()
    {
        var outcome = new WaitOutcome(WaitOutcomeKind.Break, new BreakEvent
        {
            Seq = 1,
            Snapshot = new BreakSnapshot { Reason = BreakReason.Breakpoint, Args = [new VariableInfo { Name = "order", Type = "Order", Value = "{Id=7}" }] },
        });

        var json = System.Text.Json.JsonSerializer.SerializeToElement(ToolResponse.FromOutcome(outcome), WebJson);

        var snapshot = json.GetProperty("snapshot");
        Assert.False(snapshot.TryGetProperty("args", out _));
        Assert.Equal("order", snapshot.GetProperty("data").GetProperty("args")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Client_identity_keeps_the_first_real_name()
    {
        var id = new ClientIdentity();
        Assert.Equal("vsmcpdebugger", id.Name);
        id.Capture("  ");
        id.Capture("claude-code");
        id.Capture("other");
        Assert.Equal("claude-code", id.Name);
    }

    [Fact]
    public void Ide_log_level_applies_unless_the_environment_overrides_it()
    {
        var levels = new LogLevelControl(new Serilog.Core.LoggingLevelSwitch(), overridden: false);
        levels.Apply(LogVerbosity.Debug);
        Assert.Equal(Serilog.Events.LogEventLevel.Debug, levels.Current);

        var fixedLevels = new LogLevelControl(new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Warning), overridden: true);
        fixedLevels.Apply(LogVerbosity.Debug);
        Assert.Equal(Serilog.Events.LogEventLevel.Warning, fixedLevels.Current);
    }

    [Fact]
    public async Task After_disconnect_ai_the_next_call_says_ai_disabled_and_keeps_the_selection()
    {
        var bridge = new FakeDebugBridge();
        var server = new PipeTestServer(bridge);
        var session = new SessionState();
        await using var manager = new ConnectionManager(
            () => [server.Record],
            (record, s, ct) => BridgeConnection.ConnectAsync(record, s, NullLogger.Instance, ct),
            session, NullLogger.Instance);
        await manager.GetBridgeAsync(CancellationToken.None);

        await session.OnAccessRevokedAsync();
        await server.DropAsync();
        await WaitUntil(() => !session.IsConnected);

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));
        Assert.Equal(BridgeErrorCode.AiDisabled, ex.Code);
        Assert.Contains("Reconnect AI", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handshake_refused_with_ai_disabled_keeps_its_code_and_hint()
    {
        var bridge = new FakeDebugBridge
        {
            HandshakeFailure = new LocalRpcException("AI access is disconnected in Visual Studio.")
            {
                ErrorCode = (int)BridgeErrorCode.AiDisabled,
                ErrorData = new BridgeErrorData { Hint = "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI." },
            },
        };
        await using var server = new PipeTestServer(bridge);

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() =>
            BridgeConnection.ConnectAsync(server.Record, new SessionState(), NullLogger.Instance, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.AiDisabled, ex.Code);
        Assert.Contains("Reconnect AI", ex.Hint, StringComparison.Ordinal);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
