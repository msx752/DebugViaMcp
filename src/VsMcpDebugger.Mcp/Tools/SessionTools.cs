using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VsMcpDebugger.Mcp.Connection;

namespace VsMcpDebugger.Mcp.Tools;

[McpServerToolType]
public sealed class SessionTools(IConnectionManager connections)
{
    [McpServerTool(Name = "vs_instances", ReadOnly = true), Description("Lists running Visual Studio instances that have the VsMcpDebugger extension.")]
    public Task<CallToolResult> Instances() => ToolResponse.RunAsync(connections, "vs_instances", _ =>
    {
        var current = connections.Current?.Pid;
        var list = connections.ListInstances().Select(r => new
        {
            pid = r.Pid, solution = r.SolutionPath, vsVersion = r.VsVersion, vsixVersion = r.VsixVersion, selected = r.Pid == current,
        });
        return Task.FromResult(ToolResponse.Ok(new { instances = list }));
    });

    [McpServerTool(Name = "vs_select"), Description("Chooses which Visual Studio instance to control.")]
    public Task<CallToolResult> Select([Description("Process id from vs_instances.")] int pid, CancellationToken ct) => ToolResponse.RunAsync(connections, "vs_select", async _ =>
    {
        var h = await connections.SelectAsync(pid, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { pid, solution = h.SolutionPath, vsVersion = h.VsVersion, mode = h.Mode });
    });

    [McpServerTool(Name = "debug_status", ReadOnly = true), Description("Returns the debugger mode (design, run, break) and, when stopped, where and why.")]
    public Task<CallToolResult> Status(CancellationToken ct) => ToolResponse.RunAsync(connections, "debug_status", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var status = await bridge.GetStatusAsync(ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { mode = status.Mode, snapshot = status.Snapshot });
    });
}
