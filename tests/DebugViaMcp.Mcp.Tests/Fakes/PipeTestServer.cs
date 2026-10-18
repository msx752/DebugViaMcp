using System.IO.Pipes;
using StreamJsonRpc;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Tests.Fakes;

/// <summary>Real named pipe + StreamJsonRpc server hosting a FakeDebugBridge, so tests exercise the wire format.</summary>
public sealed class PipeTestServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream _stream;
    private JsonRpc? _rpc;

    public PipeTestServer(FakeDebugBridge bridge)
    {
        Bridge = bridge;
        PipeName = ProtocolInfo.PipeNamePrefix + Guid.NewGuid().ToString("N");
        _stream = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Accepted = AcceptAsync();
    }

    public FakeDebugBridge Bridge { get; }
    public string PipeName { get; }
    public Task Accepted { get; }

    public InstanceRecord Record => new()
    {
        Pid = Environment.ProcessId, PipeName = PipeName, Token = Bridge.ExpectedToken,
        ProtocolMajor = ProtocolInfo.Major, VsVersion = "18.10", VsixVersion = "0.1.0",
    };

    public async Task NotifyBreakAsync(BreakEvent e)
    {
        await Accepted;
        await _rpc!.NotifyAsync(nameof(IDebugBridgeEvents.OnBreakAsync), e);
    }

    public async Task DropAsync()
    {
        await Accepted;
        _rpc!.Dispose();
        await _stream.DisposeAsync();
    }

    private async Task AcceptAsync()
    {
        await _stream.WaitForConnectionAsync();
        _rpc = RpcFactory.Create(_stream);
        _rpc.AddLocalRpcTarget(Bridge);
        _rpc.StartListening();
    }

    public async ValueTask DisposeAsync()
    {
        _rpc?.Dispose();
        await _stream.DisposeAsync();
    }
}
