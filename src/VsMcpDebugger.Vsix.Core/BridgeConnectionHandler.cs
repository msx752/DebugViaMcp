using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// One per pipe connection. Rejects everything until a handshake with the right token succeeds, and only
/// then calls <c>onAuthenticated</c> (which registers the connection for debugger events).
/// </summary>
public sealed class BridgeConnectionHandler(DebugBridgeService service, string token, ILog log, Action? onAuthenticated = null) : IDebugBridge
{
    private volatile bool _authenticated;

    public async Task<HandshakeResponse> HandshakeAsync(HandshakeRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw Rpc(BridgeErrorCode.InvalidArgument, "Missing handshake request.", null);

        if (!TokenEquals(request.Token, token))
        {
            log.Warn("Handshake rejected: invalid token");
            throw Rpc(BridgeErrorCode.Unauthorized, "Invalid token.", null);
        }

        if (request.ProtocolMajor != ProtocolInfo.Major)
        {
            log.Warn($"Handshake rejected: protocol {request.ProtocolMajor}.x");
            throw Rpc(BridgeErrorCode.VersionMismatch, $"VSIX speaks protocol {ProtocolInfo.Major}.x.", "Update VsMcpDebugger.Mcp and the extension to the same release.");
        }

        HandshakeResponse response;
        try
        {
            response = await service.GetHandshakeInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeException e)
        {
            throw Rpc(e.Code, e.Message, e.Hint);
        }
#pragma warning disable CA1031 // Report as internal, never as an authentication failure.
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            log.Error("Handshake failed while reading Visual Studio state", e);
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the VsMcpDebugger output pane in Visual Studio.");
        }

        if (!_authenticated)
        {
            _authenticated = true;
            onAuthenticated?.Invoke();
        }

        log.Info($"Client '{request.ClientName}' connected");
        return response;
    }

    public Task<StatusInfo> GetStatusAsync(CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.GetStatusAsync(cancellationToken));
    public Task<BreakpointInfo[]> ListBreakpointsAsync(CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.ListBreakpointsAsync(cancellationToken));
    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.SetBreakpointAsync(spec, cancellationToken));
    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.RemoveBreakpointsAsync(id, all, cancellationToken));
    public Task<long> StartAsync(CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.StartAsync(cancellationToken));
    public Task StopAsync(CallContext context, CancellationToken cancellationToken) => Guard(context, async () => { await service.StopAsync(cancellationToken).ConfigureAwait(false); return true; });
    public Task<long> ContinueAsync(CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.ContinueAsync(cancellationToken));
    public Task<long> PauseAsync(CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.PauseAsync(cancellationToken));
    public Task<long> StepAsync(StepKind kind, CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.StepAsync(kind, cancellationToken));
    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.GetStackAsync(maxFrames, cancellationToken));
    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.GetVariablesAsync(scope, path, offset, maxItems, cancellationToken));
    public Task<EvaluateResult> EvaluateAsync(string expression, CallContext context, CancellationToken cancellationToken) => Guard(context, () => service.EvaluateAsync(expression, cancellationToken));

    private async Task<T> Guard<T>(CallContext? context, Func<Task<T>> call)
    {
        if (!_authenticated) throw Rpc(BridgeErrorCode.Unauthorized, "Handshake required.", null);
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (BridgeException e)
        {
            throw Rpc(e.Code, e.Message, e.Hint);
        }
#pragma warning disable CA1031 // Every unexpected failure must reach the client as a coded error, not a dropped connection.
        catch (Exception e) when (e is not LocalRpcException and not OperationCanceledException)
#pragma warning restore CA1031
        {
            log.Error($"[{context?.CallId}] Unexpected bridge failure", e);
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the VsMcpDebugger output pane in Visual Studio.");
        }
    }

    private static LocalRpcException Rpc(BridgeErrorCode code, string message, string? hint, string? reason = null) =>
        new(message) { ErrorCode = (int)code, ErrorData = new BridgeErrorData { Hint = hint, Reason = reason } };

    /// <summary>Constant-time comparison (netstandard2.0 has no CryptographicOperations.FixedTimeEquals).</summary>
    private static bool TokenEquals(string? a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a ?? "");
        var y = Encoding.UTF8.GetBytes(b);
        var diff = x.Length ^ y.Length;
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++) diff |= x[i] ^ y[i];
        return diff == 0;
    }
}
