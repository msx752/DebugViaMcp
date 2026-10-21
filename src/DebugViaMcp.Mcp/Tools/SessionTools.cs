using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Mcp.Logging;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Tools;

[McpServerToolType]
public sealed class SessionTools(IConnectionManager connections)
{
    [McpServerTool(Name = "diag", ReadOnly = true), Description("Recent lines of this MCP server's own log (JSON), for diagnosing connection or tool problems. Contains no program values.")]
    public Task<CallToolResult> Diag([Description("Number of lines (1-500).")] int tail = 50) => ToolResponse.RunAsync(connections, "diag", _ =>
    {
        var (file, lines) = DiagnosticsReader.Tail(DiscoveryPaths.LogsDirectory, tail);
        return Task.FromResult(ToolResponse.Ok(new { file, lines }));
    });

    [McpServerTool(Name = "vs_instances", ReadOnly = true), Description("Lists running Visual Studio instances that have the Debugging via MCP extension.")]
    public Task<CallToolResult> Instances() => ToolResponse.RunAsync(connections, "vs_instances", _ =>
    {
        var current = connections.Current?.Pid;
        var list = connections.ListInstances().Select(r => new
        {
            pid = r.Pid, solution = r.SolutionPath, vsVersion = r.VsVersion, vsixVersion = r.VsixVersion, selected = r.Pid == current,
            availability = r.LeaseHolder is null ? "free" : "in_use", heldBy = r.LeaseHolder, heldByFolder = r.LeaseHolderFolder,
            matchesFolder = FolderMatch.Relates(connections.WorkingDirectory, r.SolutionPath),
        });
        return Task.FromResult(ToolResponse.Ok(new { instances = list }));
    });

    [McpServerTool(Name = "vs_select"), Description("Chooses which Visual Studio instance to control.")]
    public Task<CallToolResult> Select([Description("Process id from vs_instances.")] int pid, CancellationToken ct) => ToolResponse.RunAsync(connections, "vs_select", async _ =>
    {
        var h = await connections.SelectAsync(pid, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { pid, solution = h.SolutionPath, vsVersion = h.VsVersion, mode = h.Mode });
    });

    [McpServerTool(Name = "vs_release"), Description("Call when you are finished with this Visual Studio so another agent can use it. Visual Studio is held by one agent from its first change (breakpoint, build, start, attach) until you call this, disconnect or the user chooses Disconnect AI.")]
    public Task<CallToolResult> Release(CancellationToken ct) => ToolResponse.RunAsync(connections, "vs_release", async ctx =>
    {
        // Nothing to give up without a connection; never connect just to let go.
        if (connections.TryGetBridge() is not { } bridge) return ToolResponse.Ok(new { released = true, note = "not connected" });
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorM31) is { } unsupported) return unsupported;
        await bridge.ReleaseAsync(ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { released = true });
    });

    public const int DetailFrames = 10;
    public const int DetailLocals = 30;

    public Task<CallToolResult> Status(CancellationToken ct) => Status(false, ct);

    [McpServerTool(Name = "debug_status", ReadOnly = true), Description("Returns the debugger mode (design, run, break), what the user allows (permissions), and, when stopped, where and why. With detail=true and the program stopped it also returns the top frames of the Call Stack, the Locals (first 30) and the exception, if the break is about one: where you are and what is going on in one call. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> Status(
        [Description("When stopped: also return the Call Stack (top 10 frames), the Locals and the exception.")] bool detail = false,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "debug_status", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var status = await bridge.GetStatusAsync(ctx, ct).ConfigureAwait(false);
        connections.Session.Sync(status.Mode, status.Seq);
        var restored = connections.Session.TakeConnectionRestored();
        var more = detail && status.Mode == DebuggerMode.Break ? await ReadDetailAsync(bridge, ctx, ct).ConfigureAwait(false) : null;
        return ToolResponse.Ok(new
        {
            mode = status.Mode, permissions = status.Permissions, snapshot = ToolResponse.Snapshot(status.Snapshot), heldBy = status.LeaseHolder,
            detail = more,
            connectionRestored = restored ? true : (bool?)null,
            connectionRestoredNote = restored ? "The connection to Visual Studio was lost and re-established: any hold on it and any approval from before are gone, and breakpoints you set earlier may need checking." : null,
        });
    });

    /// <summary>
    /// The parts of "where am I and what is going on". A part Visual Studio cannot read is named in notes and the rest still comes;
    /// a refusal by the user (denied, in use, wrong mode) is not one of those and fails the call as it would on its own.
    /// </summary>
    private async Task<object> ReadDetailAsync(IDebugBridge bridge, CallContext ctx, CancellationToken ct)
    {
        var notes = new List<string>();
        StackFrameInfo[]? stack = null;
        VariableInfo[]? locals = null;
        var localsHasMore = false;
        ExceptionInfo? exception = null;

        try
        {
            stack = connections.RemoteProtocolMinor < ProtocolInfo.MinorM3
                ? await bridge.GetStackAsync(DetailFrames, ctx, ct).ConfigureAwait(false)
                : (await bridge.GetThreadStackAsync(null, DetailFrames, ctx, ct).ConfigureAwait(false)).Frames;
        }
        catch (McpBridgeException e) when (Readable(e)) { notes.Add("stack: " + e.Message); }

        try
        {
            var items = await bridge.GetVariablesAsync(VariableScope.Locals, null, 0, DetailLocals + 1, ctx, ct).ConfigureAwait(false);
            localsHasMore = items.Length > DetailLocals;
            locals = items.Take(DetailLocals).ToArray();
        }
        catch (McpBridgeException e) when (Readable(e)) { notes.Add("locals: " + e.Message); }

        if (connections.RemoteProtocolMinor >= ProtocolInfo.MinorM3)
        {
            try
            {
                exception = await bridge.GetExceptionAsync(ctx, ct).ConfigureAwait(false);
            }
            catch (McpBridgeException e) when (Readable(e)) { notes.Add("exception: " + e.Message); }
        }

        return new { stack, locals, localsHasMore = localsHasMore ? true : (bool?)null, exception, notes = notes.Count > 0 ? notes : null };
    }

    private static bool Readable(McpBridgeException e) => e.Code is BridgeErrorCode.EvalFailed or BridgeErrorCode.InvalidArgument or BridgeErrorCode.NotFound or BridgeErrorCode.Internal;
}
