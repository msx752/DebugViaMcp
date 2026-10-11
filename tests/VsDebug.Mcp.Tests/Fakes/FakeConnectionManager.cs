using VsDebug.Mcp.Connection;
using VsDebug.Mcp.Session;
using VsDebug.Protocol;

namespace VsDebug.Mcp.Tests.Fakes;

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

    public IReadOnlyList<InstanceRecord> ListInstances() => [Current!];

    public Task<HandshakeResponse> SelectAsync(int pid, CancellationToken ct) =>
        Bridge.HandshakeAsync(new HandshakeRequest { Token = Bridge.ExpectedToken }, ct);

    public Task<IDebugBridge> GetBridgeAsync(CancellationToken ct) =>
        FailWith is { } e ? Task.FromException<IDebugBridge>(e) : Task.FromResult<IDebugBridge>(Bridge);
}
