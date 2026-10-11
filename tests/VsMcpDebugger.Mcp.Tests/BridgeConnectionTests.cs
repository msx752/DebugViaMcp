using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Mcp.Tests;

public class BridgeConnectionTests
{
    [Fact]
    public async Task Connect_performs_handshake_and_resets_session()
    {
        await using var server = new PipeTestServer(new FakeDebugBridge { Mode = DebuggerMode.Run, Seq = 42 });
        var session = new SessionState();

        await using var connection = await BridgeConnection.ConnectAsync(server.Record, session, NullLogger.Instance, CancellationToken.None);

        Assert.Equal("18.10", connection.Handshake.VsVersion);
        Assert.True(session.IsConnected);
        Assert.Equal(DebuggerMode.Run, session.Mode);
        Assert.Equal(42, session.LastSeq);
    }

    [Fact]
    public async Task Pushed_break_event_reaches_session()
    {
        await using var server = new PipeTestServer(new FakeDebugBridge { Seq = 1 });
        var session = new SessionState();
        await using var connection = await BridgeConnection.ConnectAsync(server.Record, session, NullLogger.Instance, CancellationToken.None);

        var wait = session.WaitForBreakAfterAsync(1, TimeSpan.FromSeconds(5), CancellationToken.None);
        await server.NotifyBreakAsync(new BreakEvent { Seq = 2, Snapshot = new BreakSnapshot { Reason = BreakReason.Breakpoint } });

        var outcome = await wait;
        Assert.Equal(WaitOutcomeKind.Break, outcome.Kind);
        Assert.Equal(BreakReason.Breakpoint, outcome.Break!.Snapshot.Reason);
    }

    [Fact]
    public async Task Major_version_mismatch_is_rejected()
    {
        await using var server = new PipeTestServer(new FakeDebugBridge { ProtocolMajor = ProtocolInfo.Major + 1 });

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() =>
            BridgeConnection.ConnectAsync(server.Record, new SessionState(), NullLogger.Instance, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.VersionMismatch, ex.Code);
        Assert.Contains("update", ex.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_token_is_unauthorized()
    {
        await using var server = new PipeTestServer(new FakeDebugBridge());
        var record = server.Record;
        record.Token = "wrong";

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() =>
            BridgeConnection.ConnectAsync(record, new SessionState(), NullLogger.Instance, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.Unauthorized, ex.Code);
    }

    /// <summary>
    /// Pipe-squatting guard: if the process serving the pipe is not the pid recorded in the instance
    /// file, the token must never be sent.
    /// </summary>
    [Fact]
    public async Task Pipe_served_by_unexpected_process_is_rejected_before_token_is_sent()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Server-pid verification is Windows-only; Unix relies on a 0700 directory.");
        await using var server = new PipeTestServer(new FakeDebugBridge());
        var record = server.Record;
        record.Pid = Environment.ProcessId + 1;

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() =>
            BridgeConnection.ConnectAsync(record, new SessionState(), NullLogger.Instance, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.Unauthorized, ex.Code);
        Assert.DoesNotContain(nameof(FakeDebugBridge.HandshakeAsync), server.Bridge.Calls);
    }

    /// <summary>Found by the M1 end-to-end run: an internal VSIX failure was reported as "unauthorized, restart VS".</summary>
    [Fact]
    public async Task Internal_handshake_failure_is_not_reported_as_unauthorized()
    {
        await using var server = new PipeTestServer(new FakeDebugBridge
        {
            HandshakeFailure = new StreamJsonRpc.LocalRpcException("boom") { ErrorCode = (int)BridgeErrorCode.Internal, ErrorData = "See the VsMcpDebugger output pane in Visual Studio." },
        });

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() =>
            BridgeConnection.ConnectAsync(server.Record, new SessionState(), NullLogger.Instance, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.Internal, ex.Code);
        Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
        Assert.Equal("See the VsMcpDebugger output pane in Visual Studio.", ex.Hint);
    }

    /// <summary>
    /// Real-VS finding: StreamJsonRpc reserves -32000..-32003. WrongMode was -32003, so over the pipe it surfaced as
    /// RemoteSerializationException and the agent saw a generic SDK error instead of wrong_mode.
    /// NotConnected and AmbiguousInstance are raised by the MCP server itself and never sent over the pipe.
    /// </summary>
    [Theory]
    [InlineData(BridgeErrorCode.WrongMode)]
    [InlineData(BridgeErrorCode.Busy)]
    [InlineData(BridgeErrorCode.VersionMismatch)]
    [InlineData(BridgeErrorCode.Unauthorized)]
    [InlineData(BridgeErrorCode.InvalidArgument)]
    [InlineData(BridgeErrorCode.EvalFailed)]
    [InlineData(BridgeErrorCode.NotFound)]
    [InlineData(BridgeErrorCode.Denied)]
    [InlineData(BridgeErrorCode.AiDisabled)]
    [InlineData(BridgeErrorCode.Stale)]
    [InlineData(BridgeErrorCode.Unsupported)]
    [InlineData(BridgeErrorCode.Internal)]
    public async Task Every_code_the_extension_sends_reaches_the_server_as_a_coded_error(BridgeErrorCode code)
    {
        var fake = new FakeDebugBridge();
        await using var server = new PipeTestServer(fake);
        await using var connection = await BridgeConnection.ConnectAsync(server.Record, new SessionState(), NullLogger.Instance, CancellationToken.None);
        fake.OnStatus = () => throw new LocalRpcException("nope") { ErrorCode = (int)code, ErrorData = new BridgeErrorData { Hint = "try this" } };

        var ex = await Assert.ThrowsAsync<RemoteInvocationException>(() => connection.Bridge.GetStatusAsync(new CallContext { CallId = "c-000001" }, CancellationToken.None));

        Assert.Equal(code, BridgeErrors.Read(ex).Code);
        Assert.Equal("try this", BridgeErrors.Read(ex).Hint);
    }

    [Fact]
    public async Task Missing_pipe_fails_with_not_connected()
    {
        var record = new InstanceRecord { Pid = 1, PipeName = "vsmcpdebugger-does-not-exist-" + Guid.NewGuid().ToString("N"), Token = "x" };

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() =>
            BridgeConnection.ConnectAsync(record, new SessionState(), NullLogger.Instance, CancellationToken.None, connectTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Equal(BridgeErrorCode.NotConnected, ex.Code);
    }

    [Fact]
    public async Task Disconnect_marks_session_and_next_call_fails_fast()
    {
        var server = new PipeTestServer(new FakeDebugBridge());
        var session = new SessionState();
        await using var manager = new ConnectionManager(
            () => [server.Record],
            (record, s, ct) => BridgeConnection.ConnectAsync(record, s, NullLogger.Instance, ct),
            session,
            NullLogger.Instance);
        await manager.GetBridgeAsync(CancellationToken.None);

        await server.DropAsync();
        await WaitUntil(() => !session.IsConnected, TimeSpan.FromSeconds(5));

        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));
        Assert.Equal(BridgeErrorCode.NotConnected, ex.Code);
        Assert.Contains("vs_select", ex.Hint, StringComparison.Ordinal);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    /// <summary>M2 review minor 8 (host side): the pipe closed before OnAccessRevoked was read.</summary>
    [Fact]
    public async Task Lost_pipe_after_disconnect_ai_reports_ai_disabled_and_keeps_the_selection()
    {
        var server = new PipeTestServer(new FakeDebugBridge());
        var session = new SessionState();
        var connects = 0;
        await using var manager = new ConnectionManager(
            () => [server.Record],
            (record, s, ct) => ++connects == 1
                ? BridgeConnection.ConnectAsync(record, s, NullLogger.Instance, ct)
                : throw new McpBridgeException(BridgeErrorCode.AiDisabled, "Handshake with Visual Studio failed: AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI."),
            session,
            NullLogger.Instance);
        await manager.GetBridgeAsync(CancellationToken.None);

        await server.DropAsync();
        await WaitUntil(() => !session.IsConnected, TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));
        var again = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));

        Assert.Equal(BridgeErrorCode.AiDisabled, ex.Code);
        Assert.Equal(BridgeErrorCode.AiDisabled, again.Code);   // still the same instance: the selection was kept
        Assert.Equal(3, connects);
    }

    [Fact]
    public async Task Remote_minor_comes_from_the_handshake()
    {
        await using var server = new PipeTestServer(new FakeDebugBridge { ProtocolMinor = 7 });
        await using var manager = new ConnectionManager(() => [server.Record],
            (r, s, ct) => BridgeConnection.ConnectAsync(r, s, NullLogger.Instance, ct), new SessionState(), NullLogger.Instance);

        Assert.Equal(0, manager.RemoteProtocolMinor);
        await manager.GetBridgeAsync(CancellationToken.None);

        Assert.Equal(7, manager.RemoteProtocolMinor);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
