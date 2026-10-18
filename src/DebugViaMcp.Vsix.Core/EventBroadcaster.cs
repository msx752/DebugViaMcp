using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamJsonRpc;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Core;

/// <summary>
/// Sends debugger events to the authenticated clients as JSON-RPC notifications (fire-and-forget, never blocks the UI
/// thread). While an agent holds the Visual Studio, only that agent receives them.
/// </summary>
public sealed class EventBroadcaster(ILog log, InstanceLease? lease = null)
{
    private readonly object _gate = new();
    private readonly List<(JsonRpc Rpc, int Minor, int ConnectionId)> _clients = [];

    public int ConnectionCount
    {
        get { lock (_gate) return _clients.Count; }
    }

    public void Add(JsonRpc rpc, int clientMinor, int connectionId)
    {
        if (rpc is null) throw new ArgumentNullException(nameof(rpc));
        lock (_gate) _clients.Add((rpc, clientMinor, connectionId));
        rpc.Disconnected += (_, _) => { lock (_gate) _clients.RemoveAll(c => c.Rpc == rpc); };
    }

    public void PublishMode(ModeChangedEvent e) => Publish(nameof(IDebugBridgeEvents.OnModeChangedAsync), _ => e);

    public void PublishBreak(BreakEvent e) => Publish(nameof(IDebugBridgeEvents.OnBreakAsync), minor => ClientCompat.ForClient(e, minor));

    public void PublishFrameChanged(FrameChangedEvent e) =>
        Publish(nameof(IDebugBridgeEvents.OnFrameChangedAsync), minor => ClientCompat.ReceivesFrameEvents(minor) ? e : null);

    /// <param name="payloadFor">The payload for a client of the given minor; null skips that client.</param>
    private void Publish(string method, Func<int, object?> payloadFor)
    {
        (JsonRpc Rpc, int Minor, int ConnectionId)[] clients;
        lock (_gate) clients = _clients.ToArray();
        var recipients = SelectRecipients(clients.Select(c => c.ConnectionId).ToArray(), lease?.Holder);
        foreach (var (rpc, minor, connectionId) in clients)
        {
            if (!recipients.Contains(connectionId)) continue;
            var payload = payloadFor(minor);
            if (payload is null) continue;
            _ = rpc.NotifyAsync(method, payload).ContinueWith(
                t => log.Warn($"Event {method} not delivered: {t.Exception?.GetBaseException().Message}"),
                CancellationTokenNone,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    /// <summary>Every connection while the Visual Studio is free; only the holder's while an agent holds it (nobody if the holder is gone).</summary>
    internal static IReadOnlyList<int> SelectRecipients(IReadOnlyList<int> connectionIds, LeaseHolder? holder) =>
        holder is null ? connectionIds : connectionIds.Where(id => id == holder.ConnectionId).ToList();

    private static readonly System.Threading.CancellationToken CancellationTokenNone = System.Threading.CancellationToken.None;
}
