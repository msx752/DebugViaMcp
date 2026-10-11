using Microsoft.Extensions.Logging.Abstractions;
using VsDebug.Mcp.Connection;
using VsDebug.Mcp.Session;
using VsDebug.Mcp.Tests.Fakes;
using VsDebug.Protocol;
using Xunit;

namespace VsDebug.Mcp.Tests;

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

    [Fact]
    public async Task Missing_pipe_fails_with_not_connected()
    {
        var record = new InstanceRecord { Pid = 1, PipeName = "vsdebug-does-not-exist-" + Guid.NewGuid().ToString("N"), Token = "x" };

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
            session);
        await manager.GetBridgeAsync(CancellationToken.None);

        await server.DropAsync();
        await WaitUntil(() => !session.IsConnected, TimeSpan.FromSeconds(5));

        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));
        Assert.Equal(BridgeErrorCode.NotConnected, ex.Code);
        Assert.Contains("vs_select", ex.Hint, StringComparison.Ordinal);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
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
