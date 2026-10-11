using System.Collections.Generic;
using System.Threading.Tasks;
using StreamJsonRpc;
using VsDebug.Protocol;

namespace VsDebug.Vsix.Core;

/// <summary>Sends debugger events to every authenticated client as JSON-RPC notifications (fire-and-forget, never blocks the UI thread).</summary>
public sealed class EventBroadcaster(ILog log)
{
    private readonly object _gate = new();
    private readonly List<JsonRpc> _clients = [];

    public int ConnectionCount
    {
        get { lock (_gate) return _clients.Count; }
    }

    public void Add(JsonRpc rpc)
    {
        if (rpc is null) throw new System.ArgumentNullException(nameof(rpc));
        lock (_gate) _clients.Add(rpc);
        rpc.Disconnected += (_, _) => { lock (_gate) _clients.Remove(rpc); };
    }

    public void PublishMode(ModeChangedEvent e) => Publish(nameof(IDebugBridgeEvents.OnModeChangedAsync), e);

    public void PublishBreak(BreakEvent e) => Publish(nameof(IDebugBridgeEvents.OnBreakAsync), e);

    private void Publish(string method, object payload)
    {
        JsonRpc[] targets;
        lock (_gate) targets = _clients.ToArray();
        foreach (var rpc in targets)
        {
            _ = rpc.NotifyAsync(method, payload).ContinueWith(
                t => log.Warn($"Event {method} not delivered: {t.Exception?.GetBaseException().Message}"),
                CancellationTokenNone,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    private static readonly System.Threading.CancellationToken CancellationTokenNone = System.Threading.CancellationToken.None;
}
