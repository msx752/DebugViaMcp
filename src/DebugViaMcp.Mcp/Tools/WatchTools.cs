using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Tools;

/// <summary>The agent's own watch list: expressions read at every stop and returned with it (what the Watch window does for a person, without touching the person's).</summary>
[McpServerToolType]
public sealed class WatchTools(IConnectionManager connections)
{
    [McpServerTool(Name = "watch_add", Idempotent = true),
     Description("Adds an expression to your watch list. From then on every answer that reports a stop (debug_start, debug_continue, debug_step, debug_run_to, debug_wait, debug_status) carries 'watches': the value of each entry in the selected frame, with 'changed' true when it differs from the previous stop. Side-effect free like eval (no assignments; method calls run in a sandbox). At most 10 entries. The list is yours alone, lives in memory and ends when you release Visual Studio or disconnect; it does not touch the Watch windows of the user. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> WatchAdd(
        [Description("A C# expression, for example order.Total or items.Count.")] string expression,
        [Description("Optional label shown with the value.")] string? name = null,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "watch_add", async ctx =>
    {
        if (string.IsNullOrWhiteSpace(expression)) return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "expression is required.", "For example order.Total.");
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorWatch) is { } unsupported) return unsupported;

        var result = await bridge.AddWatchAsync(expression, string.IsNullOrWhiteSpace(name) ? null : name, ctx, ct).ConfigureAwait(false);
        if (!result.AlreadyExisted) connections.Session.AddWatches(1);
        var read = result.Watch.Value is not null || result.Watch.Error is not null;
        return ToolResponse.Ok(new
        {
            watch = result.Watch,
            alreadyExisted = result.AlreadyExisted ? true : (bool?)null,
            hint = read ? null : "The value appears at the next stop.",
        });
    });

    [McpServerTool(Name = "watch_list", ReadOnly = true),
     Description("Lists your watch list with each value read now in the selected frame (without values when the program is not stopped). " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> WatchList(CancellationToken ct = default) => ToolResponse.RunAsync(connections, "watch_list", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorWatch) is { } unsupported) return unsupported;

        var result = await bridge.ListWatchesAsync(false, ctx, ct).ConfigureAwait(false);
        connections.Session.SetWatchCount(result.Watches.Length);
        return ToolResponse.Ok(new { watches = result.Watches, hint = result.Watches.Length == 0 ? "The list is empty: add an expression with watch_add." : null });
    });

    [McpServerTool(Name = "watch_remove", Idempotent = true),
     Description("Removes one entry of your watch list by id (watch_list shows the ids), or all of them with all = true.")]
    public Task<CallToolResult> WatchRemove(
        [Description("The id of the entry, for example w1.")] string? id = null,
        [Description("True to remove every entry.")] bool all = false,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "watch_remove", async ctx =>
    {
        if (!all && string.IsNullOrWhiteSpace(id)) return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "Give the id of the entry to remove, or all = true.", "watch_list shows the ids.");
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorWatch) is { } unsupported) return unsupported;

        var result = await bridge.RemoveWatchAsync(all ? null : id, all, ctx, ct).ConfigureAwait(false);
        if (all) connections.Session.SetWatchCount(0);
        else connections.Session.AddWatches(-result.Removed.Length);
        return ToolResponse.Ok(new { removed = result.Removed });
    });
}
