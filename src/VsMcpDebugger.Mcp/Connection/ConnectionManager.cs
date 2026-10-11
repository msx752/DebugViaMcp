using Microsoft.Extensions.Logging;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Connection;

public interface IConnectionManager
{
    SessionState Session { get; }
    ILogger Logger { get; }
    InstanceRecord? Current { get; }
    IReadOnlyList<InstanceRecord> ListInstances();
    Task<HandshakeResponse> SelectAsync(int pid, CancellationToken ct);

    /// <summary>Returns the live bridge, auto-connecting when exactly one instance exists.</summary>
    Task<IDebugBridge> GetBridgeAsync(CancellationToken ct);
}

public sealed class ConnectionManager(
    Func<IReadOnlyList<InstanceRecord>> listInstances,
    Func<InstanceRecord, SessionState, CancellationToken, Task<BridgeConnection>> connect,
    SessionState session,
    ILogger logger) : IConnectionManager, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private BridgeConnection? _connection;
    private int? _selectedPid;

    public SessionState Session => session;
    public ILogger Logger => logger;
    public InstanceRecord? Current => _connection?.Instance;

    public IReadOnlyList<InstanceRecord> ListInstances() => listInstances();

    public async Task<HandshakeResponse> SelectAsync(int pid, CancellationToken ct)
    {
        var record = listInstances().FirstOrDefault(r => r.Pid == pid)
            ?? throw new McpBridgeException(BridgeErrorCode.NotFound, $"No Visual Studio instance with pid {pid}.", "Call vs_instances to see running instances.");

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DropAsync().ConfigureAwait(false);
            _connection = await connect(record, session, ct).ConfigureAwait(false);
            _selectedPid = pid;
            return _connection.Handshake;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IDebugBridge> GetBridgeAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is not null && session.AccessRevoked)
            {
                // Disconnect AI: keep the selection so the call after Reconnect AI simply reconnects.
                await DropAsync().ConfigureAwait(false);
                throw new McpBridgeException(BridgeErrorCode.AiDisabled,
                    "The user disconnected AI access in Visual Studio.",
                    "Ask the user to run Tools → VsMcpDebugger → Reconnect AI.");
            }

            if (_connection is { IsAlive: true }) return _connection.Bridge;

            var lostPid = _connection?.Instance.Pid;
            await DropAsync().ConfigureAwait(false);
            if (lostPid is not null)
            {
                _selectedPid = null;
                throw new McpBridgeException(BridgeErrorCode.NotConnected,
                    $"The connection to Visual Studio (pid {lostPid}) was lost.",
                    "Call vs_instances, then vs_select with the instance to use.");
            }

            var instances = listInstances();
            var target = _selectedPid is { } pid ? instances.FirstOrDefault(r => r.Pid == pid) : null;
            target ??= instances.Count switch
            {
                0 => throw new McpBridgeException(BridgeErrorCode.NotConnected,
                    "No Visual Studio instance with VsMcpDebugger is running.",
                    "Open Visual Studio with the VsMcpDebugger extension installed, or run `vsmcpdebugger doctor`."),
                1 => instances[0],
                _ => throw new McpBridgeException(BridgeErrorCode.AmbiguousInstance,
                    $"{instances.Count} Visual Studio instances are running.",
                    "Call vs_instances and then vs_select(pid) to choose one."),
            };

            _connection = await connect(target, session, ct).ConfigureAwait(false);
            _selectedPid = target.Pid;
            return _connection.Bridge;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DropAsync()
    {
        if (_connection is null) return;
        await _connection.DisposeAsync().ConfigureAwait(false);
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
