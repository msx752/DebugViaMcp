using Microsoft.Extensions.Logging;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tests.Fakes;

public sealed class FakeConnectionManager : IConnectionManager
{
    public FakeConnectionManager(FakeDebugBridge bridge, DebuggerMode mode = DebuggerMode.Break, long seq = 10)
    {
        Bridge = bridge;
        Session.Reset(mode, seq);
    }

    public FakeDebugBridge Bridge { get; }
    public SessionState Session { get; } = new();
    public InstanceRecord? Current { get; } = new() { Pid = 1, PipeName = "p", SolutionPath = @"C:\src\OrderDemo.sln" };
    public McpBridgeException? FailWith { get; set; }
    public int RemoteProtocolMinor { get; set; } = ProtocolInfo.Minor;
    public CapturingLogger Logger { get; } = new();
    ILogger IConnectionManager.Logger => Logger;

    public string WorkingDirectory { get; set; } = @"C:\src";
    public IReadOnlyList<InstanceRecord>? Instances { get; set; }

    public IReadOnlyList<InstanceRecord> ListInstances() => Instances ?? [Current!];

    public Task<HandshakeResponse> SelectAsync(int pid, CancellationToken ct) =>
        Bridge.HandshakeAsync(new HandshakeRequest { Token = Bridge.ExpectedToken }, ct);

    /// <summary>False simulates "no live connection": TryGetBridge returns null.</summary>
    public bool Connected { get; set; } = true;
    public int GetBridgeCalls { get; private set; }

    /// <summary>Serves this bridge (for example one behind a real pipe) instead of <see cref="Bridge"/>.</summary>
    public IDebugBridge? Override { get; set; }

    public IDebugBridge? TryGetBridge() => Connected ? Override ?? Bridge : null;

    public Task<IDebugBridge> GetBridgeAsync(CancellationToken ct)
    {
        GetBridgeCalls++;
        return FailWith is { } e ? Task.FromException<IDebugBridge>(e) : Task.FromResult(Override ?? Bridge);
    }
}
