using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;
using Xunit;
using static VsMcpDebugger.Mcp.Tests.ExecutionToolsTests;

namespace VsMcpDebugger.Mcp.Tests;

public class SessionToolsTests
{
    [Fact]
    public async Task Release_calls_the_bridge_and_reports_released()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await new SessionTools(cm).Release(CancellationToken.None));

        Assert.True(json.GetProperty("released").GetBoolean());
        Assert.Equal(1, cm.Bridge.Released);
    }

    [Fact]
    public async Task Release_without_a_connection_is_a_no_op()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { Connected = false };

        var json = Json(await new SessionTools(cm).Release(CancellationToken.None));

        Assert.True(json.GetProperty("released").GetBoolean());
        Assert.Equal("not connected", json.GetProperty("note").GetString());
        Assert.Equal(0, cm.GetBridgeCalls);   // it must not try to connect just to let go
        Assert.Equal(0, cm.Bridge.Released);
    }

    [Fact]
    public async Task Release_against_a_2_1_extension_is_unsupported()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { RemoteProtocolMinor = ProtocolInfo.MinorM3 };

        var json = Json(await new SessionTools(cm).Release(CancellationToken.None));

        Assert.Equal("unsupported", json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, cm.Bridge.Released);
    }

    [Fact]
    public async Task In_use_error_maps_to_the_in_use_code_and_keeps_the_holders_message()
    {
        var fake = new FakeDebugBridge
        {
            OnStatus = () => throw new LocalRpcException("This Visual Studio is in use by codex.") { ErrorCode = (int)BridgeErrorCode.InUse, ErrorData = new BridgeErrorData { Hint = "retry later" } },
        };
        await using var server = new PipeTestServer(fake);
        await using var connection = await BridgeConnection.ConnectAsync(server.Record, new SessionState(), NullLogger.Instance, CancellationToken.None);
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { Override = connection.Bridge };

        var json = Json(await new SessionTools(cm).Status(CancellationToken.None));

        var error = json.GetProperty("error");
        Assert.Equal("in_use", error.GetProperty("code").GetString());
        Assert.Contains("in use by codex", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("retry later", error.GetProperty("hint").GetString());
    }

    /// <summary>Final review 2: events went to another agent, so debug_status must bring this session back in step.</summary>
    [Fact]
    public async Task Status_brings_the_session_state_in_step_with_Visual_Studio()
    {
        var bridge = new FakeDebugBridge { OnStatus = () => new StatusInfo { Mode = DebuggerMode.Break, Seq = 20 } };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design, seq: 5);

        await new SessionTools(cm).Status(CancellationToken.None);

        Assert.Equal(DebuggerMode.Break, cm.Session.Mode);
        Assert.Equal(20, cm.Session.LastSeq);
    }

    [Fact]
    public async Task Status_reports_who_holds_it_and_a_restored_connection_once()
    {
        var bridge = new FakeDebugBridge { OnStatus = () => new StatusInfo { Mode = DebuggerMode.Design, Seq = 1, LeaseHolder = "claude-code" } };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);
        cm.Session.MarkConnectionRestored();

        var first = Json(await new SessionTools(cm).Status(CancellationToken.None));
        var second = Json(await new SessionTools(cm).Status(CancellationToken.None));

        Assert.Equal("claude-code", first.GetProperty("heldBy").GetString());
        Assert.True(first.GetProperty("connectionRestored").GetBoolean());
        Assert.Contains("hold", first.GetProperty("connectionRestoredNote").GetString(), StringComparison.Ordinal);
        Assert.False(second.TryGetProperty("connectionRestored", out _));
    }

    [Fact]
    public async Task Release_keeps_the_connection_usable()
    {
        var fake = new FakeDebugBridge();
        await using var server = new PipeTestServer(fake);
        await using var manager = new ConnectionManager(() => [server.Record],
            (record, session, ct) => BridgeConnection.ConnectAsync(record, session, NullLogger.Instance, ct), new SessionState(), NullLogger.Instance);
        await manager.SelectAsync(server.Record.Pid, CancellationToken.None);

        var before = manager.TryGetBridge();
        await new SessionTools(manager).Release(CancellationToken.None);
        var after = manager.TryGetBridge();

        Assert.NotNull(before);
        Assert.Same(before, after);
        Assert.Equal(1, fake.Released);
        Assert.Same(before, await manager.GetBridgeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TryGetBridge_is_null_before_any_connection_and_never_connects()
    {
        var connects = 0;
        await using var manager = new ConnectionManager(() => [new InstanceRecord { Pid = 1, PipeName = "p", Token = "t" }],
            (_, _, _) => { connects++; throw new InvalidOperationException("must not connect"); }, new SessionState(), NullLogger.Instance);

        Assert.Null(manager.TryGetBridge());
        Assert.Equal(0, connects);
    }
}
