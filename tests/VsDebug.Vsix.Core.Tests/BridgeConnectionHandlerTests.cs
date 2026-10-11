using StreamJsonRpc;
using VsDebug.Protocol;
using VsDebug.Vsix.Core;
using Xunit;

namespace VsDebug.Vsix.Core.Tests;

public class BridgeConnectionHandlerTests
{
    private static BridgeConnectionHandler Create(FakeDte? dte = null, Action? onAuthenticated = null) =>
        new(new DebugBridgeService(dte ?? new FakeDte(), new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), "secret", new NullLog(), onAuthenticated);

    /// <summary>Found by the M1 end-to-end run: the handshake read DTE state off the UI thread.</summary>
    [Fact]
    public async Task Handshake_reads_vs_state_on_the_ui_thread()
    {
        var h = Create(new FakeDte { EnforceUiThread = true });

        var r = await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);

        Assert.Equal("18.10", r.VsVersion);
        Assert.Equal(@"C:\src\Demo\Demo.sln", r.SolutionPath);
    }

    [Fact]
    public async Task Every_operation_touches_vs_only_on_the_ui_thread()
    {
        var h = Create(new FakeDte { EnforceUiThread = true });
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);

        await h.GetStatusAsync(CancellationToken.None);
        await h.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, CancellationToken.None);
        await h.ListBreakpointsAsync(CancellationToken.None);
        await h.GetStackAsync(5, CancellationToken.None);
        await h.GetVariablesAsync(VariableScope.Args, null, 0, 10, CancellationToken.None);
        await h.EvaluateAsync("x", CancellationToken.None);
        await h.StepAsync(StepKind.Over, CancellationToken.None);
        await h.RemoveBreakpointsAsync(null, all: true, CancellationToken.None);
    }

    [Fact]
    public async Task Calls_before_handshake_are_unauthorized()
    {
        var h = Create();
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => h.GetStatusAsync(CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.Unauthorized, ex.ErrorCode);
    }

    [Fact]
    public async Task Wrong_token_is_rejected()
    {
        var h = Create();
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.HandshakeAsync(new HandshakeRequest { Token = "nope", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.Unauthorized, ex.ErrorCode);
    }

    [Fact]
    public async Task Major_version_mismatch_is_rejected_with_code()
    {
        var h = Create();
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major + 1 }, CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.VersionMismatch, ex.ErrorCode);
    }

    [Fact]
    public async Task OnAuthenticated_fires_only_after_valid_handshake()
    {
        var fired = 0;
        var h = Create(onAuthenticated: () => fired++);

        await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.HandshakeAsync(new HandshakeRequest { Token = "nope", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None));
        Assert.Equal(0, fired);

        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Valid_handshake_returns_versions_and_unlocks_calls()
    {
        var h = Create();
        var r = await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major, ClientName = "t" }, CancellationToken.None);

        Assert.Equal(ProtocolInfo.Major, r.ProtocolMajor);
        Assert.Equal("0.1.0", r.VsixVersion);
        Assert.Equal(DebuggerMode.Break, (await h.GetStatusAsync(CancellationToken.None)).Mode);
    }

    [Fact]
    public async Task Bridge_exception_becomes_coded_rpc_error_with_hint()
    {
        var h = Create(new FakeDte { FileExistsFunc = _ => false });
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.SetBreakpointAsync(new BreakpointSpec { File = "missing.cs", Line = 1 }, CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.InvalidArgument, ex.ErrorCode);
        Assert.Equal("Use a path relative to the solution directory.", ex.ErrorData);
    }
}
