using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tools;

[McpServerToolType]
public sealed class BreakpointTools(IConnectionManager connections)
{
    [McpServerTool(Name = "bp_list", ReadOnly = true), Description("Lists breakpoints with id, location, condition, hit count, enabled and bound state.")]
    public Task<CallToolResult> List(CancellationToken ct) => ToolResponse.RunAsync(connections, "bp_list", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { breakpoints = await bridge.ListBreakpointsAsync(ctx, ct).ConfigureAwait(false) });
    });

    [McpServerTool(Name = "bp_set", Idempotent = true),
     Description("Sets a breakpoint at file+line or on a function. Optional condition, hit count, or logMessage (tracepoint: logs without stopping). Setting an identical breakpoint returns the existing id. A condition or logMessage runs in the program on every hit, so it " + InspectionTools.ConsentNote)]
    public Task<CallToolResult> Set(
        [Description("Source file, absolute or relative to the solution directory.")] string? file = null,
        [Description("1-based line number; required with file.")] int? line = null,
        [Description("Fully qualified method, e.g. OrderDemo.OrderService.Submit; alternative to file+line.")] string? function = null,
        [Description("C# boolean expression evaluated in the frame, e.g. total > 1000.")] string? condition = null,
        [Description("'true' = break when condition is true; 'changed' = break when its value changes.")] string conditionKind = "true",
        [Description("Hit count threshold; 0 = no hit count.")] int hitCount = 0,
        [Description("'none', 'equal', 'atLeast' or 'multiple'.")] string hitKind = "none",
        [Description("If set, logs this message instead of stopping. {expr} placeholders are evaluated.")] string? logMessage = null,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "bp_set", async ctx =>
    {
        var hasFileLine = !string.IsNullOrWhiteSpace(file) && line is > 0;
        var hasFunction = !string.IsNullOrWhiteSpace(function);
        if (hasFileLine == hasFunction)
            return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "Provide either file and line, or function.", "Example: { file: \"OrderService.cs\", line: 9 }");

        ConditionKind? conditionKindValue = conditionKind switch
        {
            "true" => ConditionKind.WhenTrue,
            "changed" => ConditionKind.WhenChanged,
            _ => null,
        };
        HitCountKind? hitKindValue = hitKind switch
        {
            "none" => HitCountKind.None,
            "equal" => HitCountKind.Equal,
            "atLeast" => HitCountKind.AtLeast,
            "multiple" => HitCountKind.Multiple,
            _ => null,
        };
        if (conditionKindValue is null || hitKindValue is null)
            return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "conditionKind must be 'true' or 'changed'; hitKind must be 'none', 'equal', 'atLeast' or 'multiple'.");

        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var result = await bridge.SetBreakpointAsync(new BreakpointSpec
        {
            File = hasFileLine ? file : null,
            Line = hasFileLine ? line : null,
            Function = hasFunction ? function : null,
            Condition = string.IsNullOrWhiteSpace(condition) ? null : condition,
            ConditionKind = conditionKindValue.Value,
            HitCount = hitCount,
            HitCountKind = hitKindValue.Value,
            LogMessage = string.IsNullOrWhiteSpace(logMessage) ? null : logMessage,
        }, ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(result);
    });

    [McpServerTool(Name = "bp_remove", Destructive = true, Idempotent = true), Description("Removes one breakpoint by id, or all breakpoints with all=true.")]
    public Task<CallToolResult> Remove(string? id = null, bool all = false, CancellationToken ct = default) => ToolResponse.RunAsync(connections, "bp_remove", async ctx =>
    {
        if (string.IsNullOrWhiteSpace(id) == !all)
            return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "Provide either id or all=true.");

        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var removed = await bridge.RemoveBreakpointsAsync(id, all, ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { removed, notFound = !all && removed == 0 });
    });
}
