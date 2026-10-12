using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Logging;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tools;

[McpServerToolType]
public sealed class SessionTools(IConnectionManager connections)
{
    [McpServerTool(Name = "diag", ReadOnly = true), Description("Recent lines of this MCP server's own log (JSON), for diagnosing connection or tool problems. Contains no program values.")]
    public Task<CallToolResult> Diag([Description("Number of lines (1-500).")] int tail = 50) => ToolResponse.RunAsync(connections, "diag", _ =>
    {
        var (file, lines) = DiagnosticsReader.Tail(DiscoveryPaths.LogsDirectory, tail);
        return Task.FromResult(ToolResponse.Ok(new { file, lines }));
    });

    [McpServerTool(Name = "vs_instances", ReadOnly = true), Description("Lists running Visual Studio instances that have the VsMcpDebugger extension.")]
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

    [McpServerTool(Name = "debug_status", ReadOnly = true), Description("Returns the debugger mode (design, run, break), what the user allows (permissions), and, when stopped, where and why. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> Status(CancellationToken ct) => ToolResponse.RunAsync(connections, "debug_status", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var status = await bridge.GetStatusAsync(ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { mode = status.Mode, permissions = status.Permissions, snapshot = ToolResponse.Snapshot(status.Snapshot) });
    });
}
