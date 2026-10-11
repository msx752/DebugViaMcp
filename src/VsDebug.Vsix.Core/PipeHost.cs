using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VsDebug.Protocol;

namespace VsDebug.Vsix.Core;

public interface IPipeStreamFactory
{
    Stream Create(string pipeName);
    Task WaitForConnectionAsync(Stream stream, CancellationToken ct);
}

/// <summary>Accept loop: each client gets its own handler (own handshake state) and is registered for events only once authenticated.</summary>
public sealed class PipeHost(string pipeName, IPipeStreamFactory factory, Func<Action, IDebugBridge> handlerFactory, EventBroadcaster events, ILog log) : IDisposable
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
            rpc.AddLocalRpcTarget(handlerFactory(() => events.Add(rpc)));
            rpc.Disconnected += (_, args) =>
            {
                log.Info($"Client disconnected: {args.Reason}");
                stream.Dispose();
            };
            rpc.StartListening();
            log.Info("Client connected to pipe");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
