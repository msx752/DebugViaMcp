using Microsoft.Extensions.Logging;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Connection;

public interface IConnectionManager
{
    SessionState Session { get; }
    ILogger Logger { get; }
    InstanceRecord? Current { get; }

    /// <summary>Working directory of this MCP server's process; decides which Visual Studio belongs to the agent.</summary>
    string WorkingDirectory { get; }

    /// <summary>Protocol minor of the connected IDE adapter (from its handshake); 0 when not connected.</summary>
    int RemoteProtocolMinor { get; }

    IReadOnlyList<InstanceRecord> ListInstances();
    Task<HandshakeResponse> SelectAsync(int pid, CancellationToken ct);

    /// <summary>The live bridge, or null when there is none. Never connects.</summary>
    IDebugBridge? TryGetBridge();

    /// <summary>Returns the live bridge, auto-connecting when exactly one instance exists.</summary>
    Task<IDebugBridge> GetBridgeAsync(CancellationToken ct);
}

public sealed class ConnectionManager(
    Func<IReadOnlyList<InstanceRecord>> listInstances,
    Func<InstanceRecord, SessionState, CancellationToken, Task<BridgeConnection>> connect,
    SessionState session,
    ILogger logger,
    string? workingDirectory = null) : IConnectionManager, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private BridgeConnection? _connection;
    private int? _selectedPid;

    public SessionState Session => session;
    public ILogger Logger => logger;
    public InstanceRecord? Current => _connection?.Instance;
    public string WorkingDirectory { get; } = workingDirectory ?? Environment.CurrentDirectory;
    public int RemoteProtocolMinor => _connection?.Handshake.ProtocolMinor ?? 0;

    public IReadOnlyList<InstanceRecord> ListInstances() => listInstances();

    public IDebugBridge? TryGetBridge() => _connection is { IsAlive: true } live ? live.Bridge : null;

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
                    "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI.");
            }

            if (_connection is { IsAlive: true }) return _connection.Bridge;

            var lostPid = _connection?.Instance.Pid;
            await DropAsync().ConfigureAwait(false);
            if (lostPid is { } lost)
            {
                if (await TryReconnectAsync(lost, ct).ConfigureAwait(false) is { } again) return again.Bridge;
                _selectedPid = null;
                throw new McpBridgeException(BridgeErrorCode.NotConnected,
                    $"The connection to Visual Studio (pid {lost}) was lost.",
                    "Call vs_instances, then vs_select with the instance to use.");
            }

            var instances = listInstances();
            var target = _selectedPid is { } pid ? instances.FirstOrDefault(r => r.Pid == pid) : null;
            target ??= ChooseByFolder(instances);

            _connection = await connect(target, session, ct).ConfigureAwait(false);
            _selectedPid = target.Pid;
            return _connection.Bridge;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// One instance is taken as is. With several, the one whose solution folder relates to the working directory is
    /// chosen when there is exactly one; otherwise the agent must pick with vs_select.
    /// </summary>
    private InstanceRecord ChooseByFolder(IReadOnlyList<InstanceRecord> instances)
    {
        if (instances.Count == 0)
            throw new McpBridgeException(BridgeErrorCode.NotConnected,
                "No Visual Studio instance with VsMcpDebugger is running.",
                "Open Visual Studio with the VsMcpDebugger extension installed, or run `vsmcpdebugger doctor`.");
        if (instances.Count == 1) return instances[0];

        var related = instances.Where(r => FolderMatch.Relates(WorkingDirectory, r.SolutionPath)).ToList();
        if (related.Count == 1) return related[0];

        var lines = string.Join("; ", instances.Select(Describe));
        throw new McpBridgeException(BridgeErrorCode.AmbiguousInstance,
            $"{instances.Count} Visual Studio instances are running and none can be chosen from the working directory: {lines}.",
            "Call vs_instances and then vs_select(pid) to choose one.");
    }

    private static string Describe(InstanceRecord r)
    {
        var solution = string.IsNullOrEmpty(r.SolutionPath) ? "no solution" : System.IO.Path.GetFileName(r.SolutionPath);
        var availability = r.LeaseHolder is { } holder ? $"in use by {holder}" : "free";
        return $"pid {r.Pid} ({solution}, {availability})";
    }

    /// <summary>How long the one reconnect attempt after a lost pipe may take.</summary>
    internal static readonly TimeSpan ReconnectProbe = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The pipe can close before Disconnect AI's notification is read (M2 review minor 8). One quick reconnect tells
    /// the cases apart. ai_disabled is reported as such and keeps the selection. Any other failure means the
    /// connection is lost (null).
    /// </summary>
    private async Task<BridgeConnection?> TryReconnectAsync(int pid, CancellationToken ct)
    {
        var record = listInstances().FirstOrDefault(r => r.Pid == pid);
        if (record is null) return null;
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probe.CancelAfter(ReconnectProbe);
        try
        {
            _connection = await connect(record, session, probe.Token).ConfigureAwait(false);
            session.MarkConnectionRestored();
            return _connection;
        }
        catch (McpBridgeException e) when (e.Code == BridgeErrorCode.AiDisabled)
        {
            throw new McpBridgeException(BridgeErrorCode.AiDisabled, "The user disconnected AI access in Visual Studio.",
                "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI.");
        }
        catch (McpBridgeException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
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
