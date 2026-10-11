using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamJsonRpc;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>Sends debugger events to every authenticated client as JSON-RPC notifications (fire-and-forget, never blocks the UI thread).</summary>
public sealed class EventBroadcaster(ILog log)
{
    private readonly object _gate = new();
    private readonly List<(JsonRpc Rpc, int Minor)> _clients = [];

    public int ConnectionCount
    {
        get { lock (_gate) return _clients.Count; }
    }

    public void Add(JsonRpc rpc, int clientMinor)
    {
        if (rpc is null) throw new ArgumentNullException(nameof(rpc));
        lock (_gate) _clients.Add((rpc, clientMinor));
        rpc.Disconnected += (_, _) => { lock (_gate) _clients.RemoveAll(c => c.Rpc == rpc); };
    }

    public void PublishMode(ModeChangedEvent e) => Publish(nameof(IDebugBridgeEvents.OnModeChangedAsync), _ => e);

    public void PublishBreak(BreakEvent e) => Publish(nameof(IDebugBridgeEvents.OnBreakAsync), minor => ClientCompat.ForClient(e, minor));

    /// <param name="payloadFor">The payload for a client of the given minor; null skips that client.</param>
    private void Publish(string method, Func<int, object?> payloadFor)
    {
        (JsonRpc Rpc, int Minor)[] targets;
        lock (_gate) targets = _clients.ToArray();
        foreach (var (rpc, minor) in targets)
        {
            var payload = payloadFor(minor);
            if (payload is null) continue;
            _ = rpc.NotifyAsync(method, payload).ContinueWith(
                t => log.Warn($"Event {method} not delivered: {t.Exception?.GetBaseException().Message}"),
                CancellationTokenNone,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    private static readonly System.Threading.CancellationToken CancellationTokenNone = System.Threading.CancellationToken.None;
}
