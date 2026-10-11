using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

public interface IPipeStreamFactory
{
    Stream Create(string pipeName);
    Task WaitForConnectionAsync(Stream stream, CancellationToken ct);
}

/// <summary>Accept loop: each client gets its own handler (own handshake state) and is registered for events only once authenticated.</summary>
public sealed class PipeHost(string pipeName, IPipeStreamFactory factory, Func<ConnectionHooks, BridgeConnectionHandler> handlerFactory, EventBroadcaster events, ILog log) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = factory.Create(pipeName);
                await factory.WaitForConnectionAsync(stream, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031 // The accept loop must survive any single failed accept.
            catch (Exception e)
#pragma warning restore CA1031
            {
                log.Error("Pipe accept failed", e);
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            var rpc = RpcFactory.Create(stream);
            var handler = handlerFactory(new ConnectionHooks(() => events.Add(rpc), () => RevokeAndCloseAsync(rpc)));
            rpc.AddLocalRpcTarget(handler);
            rpc.Disconnected += (_, args) =>
            {
                log.Info($"Client disconnected: {args.Reason}");
                handler.Dispose();
                stream.Dispose();
            };
            rpc.StartListening();
            log.Info("Client connected to pipe");
        }
    }

    /// <summary>How long a revoked client gets to receive the notification before the pipe is closed anyway.</summary>
    internal static readonly TimeSpan RevokeGrace = TimeSpan.FromSeconds(1);

    private static Task RevokeAndCloseAsync(JsonRpc rpc) =>
        NotifyThenCloseAsync(() => rpc.NotifyAsync(nameof(IDebugBridgeEvents.OnAccessRevokedAsync)), rpc.Dispose, RevokeGrace);

    /// <summary>Fail closed: the connection is closed after <paramref name="grace"/> even if the notification hangs or fails.</summary>
    internal static async Task NotifyThenCloseAsync(Func<Task> notify, Action close, TimeSpan grace)
    {
        try
        {
            var sending = notify();
            await Task.WhenAny(sending, Task.Delay(grace)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever happened to the notification, the connection must close.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            close();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
