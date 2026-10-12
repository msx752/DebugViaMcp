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

    [McpServerTool(Name = "debug_start"), Description("Starts debugging the solution's startup project (F5), or the given project when project is set; the solution's startup project is not changed. Waits up to 30 s for the first break. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> Start(
        [Description("Project to start (name or unique name); omit for the solution's startup project.")] string? project = null,
        CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(project)
            ? Resume("debug_start", DebuggerMode.Design, "Debugging is already running; use debug_continue or debug_stop.", (b, ctx) => b.StartAsync(ctx, ct), ct, check: (b, ctx) => ToolResponse.CheckBuildAsync(connections, b, null, null, ctx, ct))
            : Resume("debug_start", DebuggerMode.Design, "Debugging is already running; use debug_continue or debug_stop.", (b, ctx) => b.StartProjectAsync(project, ctx, ct), ct, ProtocolInfo.MinorM31,
                check: (b, ctx) => ToolResponse.CheckBuildAsync(connections, b, project, null, ctx, ct));

    [McpServerTool(Name = "debug_continue"), Description("Resumes execution (F5) and waits up to 30 s for the next break. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> Continue(CancellationToken ct) =>
        Resume("debug_continue", DebuggerMode.Break, "Call debug_pause or debug_wait first.", (b, ctx) => b.ContinueAsync(ctx, ct), ct);

    [McpServerTool(Name = "debug_pause"), Description("Breaks into the running program (Break All). " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> Pause(CancellationToken ct) =>
        Resume("debug_pause", DebuggerMode.Run, "The program is not running.", (b, ctx) => b.PauseAsync(ctx, ct), ct);

    [McpServerTool(Name = "debug_step"), Description("Steps 'into', 'over' or 'out' and returns where execution stopped. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> Step([Description("'into', 'over' or 'out'.")] string kind, CancellationToken ct)
    {
        StepKind? stepKind = kind switch { "into" => StepKind.Into, "over" => StepKind.Over, "out" => StepKind.Out, _ => null };
        if (stepKind is null)
            return Task.FromResult(ToolResponse.Error(BridgeErrorCode.InvalidArgument, $"Unknown step kind '{kind}'.", "Use 'into', 'over' or 'out'."));
        return Resume("debug_step", DebuggerMode.Break, "Call debug_pause or debug_wait first.", (b, ctx) => b.StepAsync(stepKind.Value, ctx, ct), ct);
    }

    [McpServerTool(Name = "debug_wait", ReadOnly = true), Description("Waits for the next break (breakpoint, exception, pause) up to timeoutSec (max 120). " + InspectionTools.UntrustedNote)]
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

    [McpServerTool(Name = "debug_run_to"),
     Description("Runs to file:line (like Run to Cursor) and waits up to 30 s for the break; stops earlier at other breakpoints. Uses a temporary breakpoint that is removed at the next break. " + InspectionTools.UntrustedNote)]
    public Task<CallToolResult> RunTo(
        [Description("Source file, absolute or relative to the solution directory.")] string file,
        [Description("1-based line number.")] int line,
        CancellationToken ct) => ToolResponse.RunAsync(connections, "debug_run_to", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        var session = connections.Session;
        if (session.Mode == DebuggerMode.Design)
            return ToolResponse.WrongMode(DebuggerMode.Break, session.Mode, "Start debugging with debug_start.");
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorM3) is { } unsupported) return unsupported;

        var r = await bridge.RunToAsync(file, line, ctx, ct).ConfigureAwait(false);
        if (r.BindError is not null)
            return ToolResponse.Ok(new { state = session.Mode, bindError = r.BindError, hint = "Pick a line with executable code." });
        session.MarkResumed(r.Seq);
        var outcome = await session.WaitForBreakAfterAsync(r.Seq, waitForBreak, ct).ConfigureAwait(false);
        return ToolResponse.Ok(ToolResponse.FromOutcome(outcome));
    });

    [McpServerTool(Name = "debug_attach"),
     Description("Attaches the debugger to a running process of yours, by pid or processName (e.g. FaultDemo). Visual Studio can then read and change that process's memory, so this " + InspectionTools.ConsentNote + " debug_stop detaches without ending the process.")]
    public Task<CallToolResult> Attach(
        [Description("Process id.")] int? pid = null,
        [Description("Process name without .exe; must match exactly one of your processes.")] string? processName = null,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "debug_attach", async ctx =>
    {
        if ((pid is null) == string.IsNullOrWhiteSpace(processName))
            return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "Provide either pid or processName.");
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (connections.Session.Mode != DebuggerMode.Design)
            return ToolResponse.WrongMode(DebuggerMode.Design, connections.Session.Mode, "Stop the current session first (debug_stop).");
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorM3) is { } unsupported) return unsupported;

        string? name = null;
        if (pid is null)
        {
            var candidates = await bridge.ListAttachableAsync(processName, ctx, ct).ConfigureAwait(false);
            switch (candidates.Length)
            {
                case 0:
                    return ToolResponse.Error(BridgeErrorCode.NotFound, $"No process of yours named '{processName}'.", "Check the name (without .exe).");
                case > 1:
                    return ToolResponse.Error(BridgeErrorCode.InvalidArgument, $"{candidates.Length} processes are named '{processName}'.",
                        $"Candidates: {string.Join(", ", candidates.Select(c => $"{c.Pid} {c.Name}"))}. Call again with pid.");
            }

            (pid, name) = (candidates[0].Pid, candidates[0].Name);
        }

        if (name is null)
        {
            // Best effort: the process name lets the stale-build check find the project.
            try
            {
                name = (await bridge.ListAttachableAsync(null, ctx, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Pid == pid)?.Name;
            }
            catch (StreamJsonRpc.RemoteInvocationException)
            {
                name = null;
            }
        }

        var warnings = name is null ? [] : await ToolResponse.CheckBuildAsync(connections, bridge, name, null, ctx, ct).ConfigureAwait(false);
        await connections.Session.IssueAsync(() => bridge.AttachAsync(pid.Value, ctx, ct)).ConfigureAwait(false);
        return ToolResponse.OkWithWarnings(new { state = "attached", pid, process = name, hint = "Set breakpoints with bp_set, then debug_wait or debug_pause. debug_stop detaches." }, warnings);
    });

    private Task<CallToolResult> Resume(string tool, DebuggerMode required, string hint, Func<IDebugBridge, CallContext, Task<long>> command, CancellationToken ct, int requiredMinor = 0,
        Func<IDebugBridge, CallContext, Task<IReadOnlyList<BuildWarning>>>? check = null) =>
        ToolResponse.RunAsync(connections, tool, async ctx =>
        {
            var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
            var session = connections.Session;
            if (session.Mode != required)
                return ToolResponse.WrongMode(required, session.Mode, hint);
            if (requiredMinor > 0 && ToolResponse.RequireMinor(connections, requiredMinor) is { } unsupported) return unsupported;

            var warnings = check is null ? [] : await check(bridge, ctx).ConfigureAwait(false);
            var seq = await session.IssueAsync(() => command(bridge, ctx)).ConfigureAwait(false);
            var outcome = await session.WaitForBreakAfterAsync(seq, waitForBreak, ct).ConfigureAwait(false);
            return ToolResponse.OkWithWarnings(ToolResponse.FromOutcome(outcome), warnings);
        });
}
