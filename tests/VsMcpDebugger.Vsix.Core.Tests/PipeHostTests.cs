using System.IO.Pipes;
using StreamJsonRpc;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class PipeHostTests
{
    private sealed class PlainPipeFactory : IPipeStreamFactory
    {
        public Stream Create(string pipeName) =>
            new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        public Task WaitForConnectionAsync(Stream stream, CancellationToken ct) => ((NamedPipeServerStream)stream).WaitForConnectionAsync(ct);
    }

    private sealed class Events : IDebugBridgeEvents
    {
        public TaskCompletionSource<BreakEvent> Got { get; } = new();
        public Task OnModeChangedAsync(ModeChangedEvent e) => Task.CompletedTask;
        public Task OnBreakAsync(BreakEvent e) { Got.TrySetResult(e); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Only_authenticated_clients_receive_broadcast_events()
    {
        var pipe = DiscoveryWriter.NewPipeName();
        var service = new DebugBridgeService(new FakeDte(), new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        var broadcaster = new EventBroadcaster(new NullLog());
        using var host = new PipeHost(pipe, new PlainPipeFactory(), onAuth => new BridgeConnectionHandler(service, "secret", new NullLog(), onAuth), broadcaster, new NullLog());
        host.Start();

        async Task<(JsonRpc Rpc, IDebugBridge Bridge, Events Events)> ConnectAsync(bool handshake = true)
        {
            var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);
            var events = new Events();
            var rpc = RpcFactory.Create(client);
            rpc.AddLocalRpcTarget(events);
            var bridge = rpc.Attach<IDebugBridge>();
            rpc.StartListening();
            if (handshake)
                await bridge.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
            return (rpc, bridge, events);
        }

        var anonymous = await ConnectAsync(handshake: false);
        var a = await ConnectAsync();
        var b = await ConnectAsync();
        Assert.Equal(2, broadcaster.ConnectionCount);   // the client without a handshake is not registered for events

        broadcaster.PublishBreak(new BreakEvent { Seq = 1, Snapshot = new BreakSnapshot { Reason = BreakReason.Pause } });

        Assert.Equal(1, (await a.Events.Got.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
        Assert.Equal(1, (await b.Events.Got.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
        await Task.Delay(200);
        Assert.False(anonymous.Events.Got.Task.IsCompleted);
        anonymous.Rpc.Dispose();
        a.Rpc.Dispose();
        b.Rpc.Dispose();
    }
}
