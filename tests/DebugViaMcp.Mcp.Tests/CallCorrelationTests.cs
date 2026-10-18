using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Mcp.Session;
using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

public class CallCorrelationTests
{
    [Fact]
    public async Task Each_tool_call_gets_a_unique_call_id_forwarded_to_the_bridge()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var tools = new SessionTools(cm);

        await tools.Status(CancellationToken.None);
        await tools.Status(CancellationToken.None);

        Assert.Equal(2, cm.Bridge.CallIds.Distinct().Count());
        Assert.All(cm.Bridge.CallIds, id => Assert.Matches(@"^c-\d{6}$", id));
    }

    [Fact]
    public async Task Completion_is_logged_inside_the_call_id_scope()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        await new SessionTools(cm).Status(CancellationToken.None);

        var id = Assert.Single(cm.Bridge.CallIds);
        var entry = Assert.Single(cm.Logger.Entries, e => e.Message.Contains("debug_status", StringComparison.Ordinal));
        Assert.Contains(id, entry.Scope, StringComparison.Ordinal);
        Assert.Contains(id, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_data_hint_and_reason_survive_the_wire()
    {
        var bridge = new FakeDebugBridge
        {
            OnStatus = () => throw new LocalRpcException("Denied by the user in Visual Studio.")
            {
                ErrorCode = (int)BridgeErrorCode.Denied,
                ErrorData = new BridgeErrorData { Hint = "Ask the user.", Reason = DeniedReasons.UserDenied },
            },
        };
        await using var server = new PipeTestServer(bridge);
        await using var manager = new ConnectionManager(
            () => [server.Record],
            (record, s, ct) => BridgeConnection.ConnectAsync(record, s, NullLogger.Instance, ct),
            new SessionState(),
            NullLogger.Instance);

        var error = Json(await new SessionTools(manager).Status(CancellationToken.None)).GetProperty("error");

        Assert.Equal("denied", error.GetProperty("code").GetString());
        Assert.Equal("user_denied", error.GetProperty("reason").GetString());
        Assert.Equal("Ask the user.", error.GetProperty("hint").GetString());
    }
}
