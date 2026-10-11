using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VsDebug.Protocol;

namespace VsDebug.Vsix.Core;

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
            throw Rpc(BridgeErrorCode.VersionMismatch, $"VSIX speaks protocol {ProtocolInfo.Major}.x.", "Update VsDebug.Mcp and the extension to the same release.");
        }

        var (mode, seq) = await service.GetModeAsync(cancellationToken).ConfigureAwait(false);
        if (!_authenticated)
        {
            _authenticated = true;
            onAuthenticated?.Invoke();
        }

        log.Info($"Client '{request.ClientName}' connected");
        return new HandshakeResponse
        {
            ProtocolMajor = ProtocolInfo.Major, ProtocolMinor = ProtocolInfo.Minor,
            VsVersion = service.VsVersion, VsixVersion = service.VsixVersion, SolutionPath = service.SolutionPath,
            Mode = mode, Seq = seq,
        };
    }

    public Task<StatusInfo> GetStatusAsync(CancellationToken cancellationToken) => Guard(() => service.GetStatusAsync(cancellationToken));
    public Task<BreakpointInfo[]> ListBreakpointsAsync(CancellationToken cancellationToken) => Guard(() => service.ListBreakpointsAsync(cancellationToken));
    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CancellationToken cancellationToken) => Guard(() => service.SetBreakpointAsync(spec, cancellationToken));
    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CancellationToken cancellationToken) => Guard(() => service.RemoveBreakpointsAsync(id, all, cancellationToken));
    public Task<long> StartAsync(CancellationToken cancellationToken) => Guard(() => service.StartAsync(cancellationToken));
    public Task StopAsync(CancellationToken cancellationToken) => Guard(async () => { await service.StopAsync(cancellationToken).ConfigureAwait(false); return true; });
    public Task<long> ContinueAsync(CancellationToken cancellationToken) => Guard(() => service.ContinueAsync(cancellationToken));
    public Task<long> PauseAsync(CancellationToken cancellationToken) => Guard(() => service.PauseAsync(cancellationToken));
    public Task<long> StepAsync(StepKind kind, CancellationToken cancellationToken) => Guard(() => service.StepAsync(kind, cancellationToken));
    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CancellationToken cancellationToken) => Guard(() => service.GetStackAsync(maxFrames, cancellationToken));
    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int maxItems, CancellationToken cancellationToken) => Guard(() => service.GetVariablesAsync(scope, path, maxItems, cancellationToken));
    public Task<EvaluateResult> EvaluateAsync(string expression, CancellationToken cancellationToken) => Guard(() => service.EvaluateAsync(expression, cancellationToken));

    private async Task<T> Guard<T>(Func<Task<T>> call)
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
            log.Error("Unexpected bridge failure", e);
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the VsDebug output pane in Visual Studio.");
        }
    }

    private static LocalRpcException Rpc(BridgeErrorCode code, string message, string? hint) =>
        new(message) { ErrorCode = (int)code, ErrorData = hint };

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
