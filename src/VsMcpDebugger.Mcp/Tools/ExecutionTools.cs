using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tools;

[McpServerToolType]
public sealed class ExecutionTools(IConnectionManager connections, TimeSpan waitForBreak)
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    /// <summary>Constructor used by DI; the two-argument one exists for tests with short waits.</summary>
    [ActivatorUtilitiesConstructor]
    public ExecutionTools(IConnectionManager connections) : this(connections, DefaultWait) { }

    public static TimeSpan ClampWait(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 120));

    [McpServerTool(Name = "debug_start"), Description("Starts debugging the solution's startup project (F5). Waits up to 30 s for the first break.")]
    public Task<CallToolResult> Start(CancellationToken ct) =>
        Resume("debug_start", DebuggerMode.Design, "Debugging is already running; use debug_continue or debug_stop.", (b, ctx) => b.StartAsync(ctx, ct), ct);

    [McpServerTool(Name = "debug_continue"), Description("Resumes execution (F5) and waits up to 30 s for the next break.")]
    public Task<CallToolResult> Continue(CancellationToken ct) =>
        Resume("debug_continue", DebuggerMode.Break, "Call debug_pause or debug_wait first.", (b, ctx) => b.ContinueAsync(ctx, ct), ct);

    [McpServerTool(Name = "debug_pause"), Description("Breaks into the running program (Break All).")]
    public Task<CallToolResult> Pause(CancellationToken ct) =>
        Resume("debug_pause", DebuggerMode.Run, "The program is not running.", (b, ctx) => b.PauseAsync(ctx, ct), ct);

    [McpServerTool(Name = "debug_step"), Description("Steps 'into', 'over' or 'out' and returns where execution stopped.")]
    public Task<CallToolResult> Step([Description("'into', 'over' or 'out'.")] string kind, CancellationToken ct)
    {
        StepKind? stepKind = kind switch { "into" => StepKind.Into, "over" => StepKind.Over, "out" => StepKind.Out, _ => null };
        if (stepKind is null)
            return Task.FromResult(ToolResponse.Error(BridgeErrorCode.InvalidArgument, $"Unknown step kind '{kind}'.", "Use 'into', 'over' or 'out'."));
        return Resume("debug_step", DebuggerMode.Break, "Call debug_pause or debug_wait first.", (b, ctx) => b.StepAsync(stepKind.Value, ctx, ct), ct);
    }

    [McpServerTool(Name = "debug_wait", ReadOnly = true), Description("Waits for the next break (breakpoint, exception, pause) up to timeoutSec (max 120).")]
    public Task<CallToolResult> Wait(int timeoutSec = 30, CancellationToken ct = default) => ToolResponse.RunAsync(connections, "debug_wait", async _ =>
    {
        await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var session = connections.Session;
        if (session.Mode == DebuggerMode.Design)
            return ToolResponse.WrongMode(DebuggerMode.Run, session.Mode, "Start debugging with debug_start.");
        // Wait for a break after the last resume command, so a break that landed after a timed-out
        // debug_continue (but before this call) is returned instead of being skipped.
        var outcome = await session.WaitForBreakAfterAsync(session.LastResumeSeq, ClampWait(timeoutSec), ct).ConfigureAwait(false);
        return ToolResponse.Ok(ToolResponse.FromOutcome(outcome));
    });

    [McpServerTool(Name = "debug_stop", Destructive = true), Description("Stops debugging (Shift+F5).")]
    public Task<CallToolResult> Stop(CancellationToken ct) => ToolResponse.RunAsync(connections, "debug_stop", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (connections.Session.Mode == DebuggerMode.Design)
            return ToolResponse.Ok(new { state = "design", message = "Not debugging." });
        await bridge.StopAsync(ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { state = "design" });
    });

    private Task<CallToolResult> Resume(string tool, DebuggerMode required, string hint, Func<IDebugBridge, CallContext, Task<long>> command, CancellationToken ct) =>
        ToolResponse.RunAsync(connections, tool, async ctx =>
        {
            var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
            var session = connections.Session;
            if (session.Mode != required)
                return ToolResponse.WrongMode(required, session.Mode, hint);

            var seq = await command(bridge, ctx).ConfigureAwait(false);
            session.MarkResumed(seq);
            var outcome = await session.WaitForBreakAfterAsync(seq, waitForBreak, ct).ConfigureAwait(false);
            return ToolResponse.Ok(ToolResponse.FromOutcome(outcome));
        });
}
