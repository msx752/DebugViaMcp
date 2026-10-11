using VsDebug.Mcp.Connection;
using VsDebug.Mcp.Session;
using VsDebug.Protocol;
using Xunit;

namespace VsDebug.Mcp.Tests;

public class ConnectionManagerTests
{
    private static InstanceRecord Rec(int pid) => new() { Pid = pid, PipeName = "p" + pid, Token = "t" };

    private static Task<BridgeConnection> NeverConnect(InstanceRecord r, SessionState s, CancellationToken ct) =>
        throw new InvalidOperationException("connect must not be called");

    [Fact]
    public async Task No_instances_gives_not_connected_with_doctor_hint()
    {
        await using var manager = new ConnectionManager(() => [], NeverConnect, new SessionState());

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotConnected, ex.Code);
        Assert.Contains("VsDebug extension", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Several_instances_without_selection_is_ambiguous()
    {
        await using var manager = new ConnectionManager(() => [Rec(1), Rec(2)], NeverConnect, new SessionState());

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.GetBridgeAsync(CancellationToken.None));

        Assert.Equal(BridgeErrorCode.AmbiguousInstance, ex.Code);
        Assert.Contains("vs_select", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_unknown_pid_is_not_found()
    {
        await using var manager = new ConnectionManager(() => [Rec(1)], NeverConnect, new SessionState());

        var ex = await Assert.ThrowsAsync<McpBridgeException>(() => manager.SelectAsync(99, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
    }
}
