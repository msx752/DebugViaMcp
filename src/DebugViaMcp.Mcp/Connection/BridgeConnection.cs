using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using DebugViaMcp.Mcp.Logging;
using DebugViaMcp.Mcp.Session;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Connection;

/// <summary>One authenticated JSON-RPC connection to one IDE instance.</summary>
public sealed class BridgeConnection : IAsyncDisposable
{
    private readonly NamedPipeClientStream _stream;
    private readonly JsonRpc _rpc;

    private BridgeConnection(InstanceRecord instance, NamedPipeClientStream stream, JsonRpc rpc, IDebugBridge bridge, HandshakeResponse handshake)
    {
        Instance = instance;
        _stream = stream;
        _rpc = rpc;
        Bridge = bridge;
        Handshake = handshake;
    }

    public InstanceRecord Instance { get; }
    public HandshakeResponse Handshake { get; }
    public IDebugBridge Bridge { get; }
    public bool IsAlive => !_rpc.IsDisposed && !_rpc.Completion.IsCompleted;

    public static async Task<BridgeConnection> ConnectAsync(
        InstanceRecord instance, SessionState session, ILogger logger, CancellationToken ct, TimeSpan? connectTimeout = null, string clientName = "debugviamcp", string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logger);

        var stream = new NamedPipeClientStream(".", instance.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(connectTimeout ?? TimeSpan.FromSeconds(5));
            await stream.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or TimeoutException && !ct.IsCancellationRequested)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new McpBridgeException(BridgeErrorCode.NotConnected,
                $"Could not connect to Visual Studio (pid {instance.Pid}).",
                "Make sure that Visual Studio instance is still running (see the troubleshooting section of the README).", e);
        }

        if (!PipeServerIdentity.IsServedBy(stream, instance.Pid))
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new McpBridgeException(BridgeErrorCode.Unauthorized,
                $"The pipe for Visual Studio pid {instance.Pid} is served by a different process; refusing to send credentials.",
                "Restart Visual Studio so it writes a fresh instance file, then retry.");
        }

        var rpc = RpcFactory.Create(stream);
        rpc.AddLocalRpcTarget<IDebugBridgeEvents>(session, null);
        var bridge = rpc.Attach<IDebugBridge>();
        rpc.Disconnected += (_, args) =>
        {
            Log.PipeDisconnected(logger, instance.Pid, args.Reason);
            session.MarkDisconnected();
        };
        rpc.StartListening();

        HandshakeResponse handshake;
        try
        {
            handshake = await bridge.HandshakeAsync(new HandshakeRequest
            {
                Token = instance.Token,
                ProtocolMajor = ProtocolInfo.Major,
                ProtocolMinor = ProtocolInfo.Minor,
                ClientName = clientName,
                ClientWorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            }, ct).ConfigureAwait(false);
        }
        catch (RemoteInvocationException e)
        {
            rpc.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
            var (_, remoteHint, _) = BridgeErrors.Read(e);
            var (code, hint) = e.ErrorCode switch
            {
                (int)BridgeErrorCode.AiDisabled => (BridgeErrorCode.AiDisabled, remoteHint ?? "Ask the user to run Extensions → Debugging via MCP → Reconnect AI."),
                (int)BridgeErrorCode.VersionMismatch => (BridgeErrorCode.VersionMismatch, "Update the Debugging via MCP extension and the debugviamcp tool to the same release."),
                (int)BridgeErrorCode.Unauthorized => (BridgeErrorCode.Unauthorized, "Restart Visual Studio so it writes a fresh instance file, then retry."),
                _ => (BridgeErrorCode.Internal, remoteHint ?? "See the Debugging via MCP output pane in Visual Studio."),
            };
            throw new McpBridgeException(code, "Handshake with Visual Studio failed: " + e.Message, hint, e);
        }

        if (handshake.ProtocolMajor != ProtocolInfo.Major)
        {
            rpc.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new McpBridgeException(BridgeErrorCode.VersionMismatch,
                $"VSIX speaks protocol {handshake.ProtocolMajor}.x, this server speaks {ProtocolInfo.Major}.x.",
                "Update the Debugging via MCP extension and the debugviamcp tool to the same release.");
        }

        session.Reset(handshake.Mode, handshake.Seq);
        session.SetLimits(handshake.MaxWaitSeconds, handshake.MaxBuildSeconds);
        Log.Connected(logger, instance.Pid, handshake.VsVersion, handshake.VsixVersion);
        return new BridgeConnection(instance, stream, rpc, bridge, handshake);
    }

    public async ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
