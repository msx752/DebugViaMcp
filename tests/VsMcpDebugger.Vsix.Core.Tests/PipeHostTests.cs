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

    private sealed class NullActivitySink : IActivitySink
    {
        public void Write(string line)
        {
        }
    }

    private sealed class Events : IDebugBridgeEvents
    {
        public TaskCompletionSource<BreakEvent> Got { get; } = new();
        public Task OnModeChangedAsync(ModeChangedEvent e) => Task.CompletedTask;
        public Task OnBreakAsync(BreakEvent e) { Got.TrySetResult(e); return Task.CompletedTask; }
        public Task OnFrameChangedAsync(FrameChangedEvent e) => Task.CompletedTask;
        public Task OnAccessRevokedAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Only_authenticated_clients_receive_broadcast_events()
    {
        var pipe = DiscoveryWriter.NewPipeName();
        var service = new DebugBridgeService(new FakeDte(), new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        var broadcaster = new EventBroadcaster(new NullLog());
        var settings = new TestSettings();
        var security = new BridgeSecurity(settings, new ConsentGate(new FakePrompt(), () => settings.ConsentTimeout),
            new AuditLog(Path.Combine(Path.GetTempPath(), "vsmcpdebugger-tests-audit"), () => 7, new NullActivitySink(), new NullLog()), new AiAccessSwitch(enabled: true));
        using var host = new PipeHost(pipe, new PlainPipeFactory(), hooks => new BridgeConnectionHandler(service, "secret", security, new NullLog(), hooks), broadcaster, new NullLog());
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

    [Fact]
    public async Task Disconnect_ai_closes_live_pipes_and_refuses_new_handshakes_until_reconnect()
    {
        var pipe = DiscoveryWriter.NewPipeName();
        var settings = new TestSettings();
        var access = new AiAccessSwitch(enabled: true);
        var security = new BridgeSecurity(settings, new ConsentGate(new FakePrompt(), () => settings.ConsentTimeout),
            new AuditLog(Path.Combine(Path.GetTempPath(), "vsmcpdebugger-tests-audit"), () => 7, new NullActivitySink(), new NullLog()), access);
        var service = new DebugBridgeService(new FakeDte(), new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        using var host = new PipeHost(pipe, new PlainPipeFactory(), hooks => new BridgeConnectionHandler(service, "secret", security, new NullLog(), hooks),
            new EventBroadcaster(new NullLog()), new NullLog());
        host.Start();

        async Task<(JsonRpc Rpc, IDebugBridge Bridge, RevokeEvents Events)> ConnectAsync()
        {
            var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);
            var events = new RevokeEvents();
            var rpc = RpcFactory.Create(client);
            rpc.AddLocalRpcTarget<IDebugBridgeEvents>(events, null);
            var bridge = rpc.Attach<IDebugBridge>();
            rpc.StartListening();
            return (rpc, bridge, events);
        }

        var first = await ConnectAsync();
        await first.Bridge.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major, ClientName = "claude-code" }, CancellationToken.None);

        security.DisconnectAi();

        await first.Events.Revoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.Rpc.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await ConnectAsync();
        var refused = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            second.Bridge.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.AiDisabled, refused.ErrorCode);

        security.ReconnectAi();
        var third = await ConnectAsync();
        await third.Bridge.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
        second.Rpc.Dispose();
        third.Rpc.Dispose();
    }

    private sealed class RevokeEvents : IDebugBridgeEvents
    {
        public TaskCompletionSource Revoked { get; } = new();
        public Task OnModeChangedAsync(ModeChangedEvent e) => Task.CompletedTask;
        public Task OnBreakAsync(BreakEvent e) => Task.CompletedTask;
        public Task OnFrameChangedAsync(FrameChangedEvent e) => Task.CompletedTask;
        public Task OnAccessRevokedAsync()
        {
            Revoked.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>Security review: a client that never reads must not keep a revoked connection open.</summary>
    [Fact]
    public async Task Revoked_connection_closes_even_if_the_notification_never_completes()
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await PipeHost.NotifyThenCloseAsync(() => new TaskCompletionSource().Task, () => closed.TrySetResult(), TimeSpan.FromMilliseconds(100));

        Assert.True(closed.Task.IsCompleted);
    }

    [Fact]
    public async Task Revoked_connection_closes_even_if_the_notification_fails()
    {
        var closed = false;

        await PipeHost.NotifyThenCloseAsync(() => Task.FromException(new IOException("pipe broken")), () => closed = true, TimeSpan.FromSeconds(1));

        Assert.True(closed);
    }
}
