using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using StreamJsonRpc;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Mcp.Logging;
using DebugViaMcp.Mcp.Session;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Tools;

/// <summary>Uniform JSON shaping and error mapping for every tool.</summary>
internal static class ToolResponse
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static CallToolResult Ok(object payload) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, Options) }],
    };

    /// <summary>Like <see cref="Ok"/>, with a <c>warnings</c> array when there is anything to warn about.</summary>
    public static CallToolResult OkWithWarnings(object payload, IReadOnlyList<BuildWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (warnings.Count == 0) return Ok(payload);
        var node = JsonSerializer.SerializeToNode(payload, Options) as JsonObject ?? new JsonObject { ["result"] = JsonSerializer.SerializeToNode(payload, Options) };
        node["warnings"] = JsonSerializer.SerializeToNode(warnings, Options);
        return new CallToolResult { Content = [new TextContentBlock { Text = node.ToJsonString(Options) }] };
    }

    /// <summary>
    /// Asks the extension about out-of-date builds in scope. Never blocks and never fails the tool, except that
    /// <c>in_use</c>, <c>denied</c> and <c>ai_disabled</c> propagate: the Visual Studio is held, or its owner refused or cut the agent off, so the tool must not run (or ask the owner again).
    /// </summary>
    public static async Task<IReadOnlyList<BuildWarning>> CheckBuildAsync(
        IConnectionManager connections, IDebugBridge bridge, string? project, string? file, CallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(bridge);
        if (connections.RemoteProtocolMinor < ProtocolInfo.MinorM31) return [];
        try
        {
            return await bridge.CheckBuildFreshnessAsync(project, file, context, ct).ConfigureAwait(false);
        }
        catch (RemoteInvocationException e) when (BridgeErrors.Read(e).Code is not (BridgeErrorCode.InUse or BridgeErrorCode.Denied or BridgeErrorCode.AiDisabled))
        {
            Log.CheckBuildFailed(connections.Logger, context.CallId, e.Message);
            return [];
        }
    }

    public static CallToolResult Error(BridgeErrorCode code, string message, string? hint = null, string? reason = null) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = new { code = BridgeErrorNames.ToName(code), message, hint, reason } }, Options) }],
    };

    /// <summary>Null when the connected extension implements protocol <paramref name="minor"/>; otherwise the 'unsupported' error to return without calling it.</summary>
    public static CallToolResult? RequireMinor(IConnectionManager connections, int minor)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var remote = connections.RemoteProtocolMinor;
        return remote >= minor ? null : Error(BridgeErrorCode.Unsupported,
            $"The Visual Studio extension speaks protocol {ProtocolInfo.Major}.{remote}; this tool needs {ProtocolInfo.Major}.{minor}.",
            "Update the Debugging via MCP extension in Visual Studio to the same release as the debugviamcp tool.");
    }

    public static CallToolResult WrongMode(DebuggerMode required, DebuggerMode current, string hint) =>
        Error(BridgeErrorCode.WrongMode, $"Requires {required} mode; current: {current}.", hint);

    /// <summary>Snapshot as the agent sees it: debuggee values under 'data' (spec §3.3).</summary>
    public static object? Snapshot(BreakSnapshot? s) => s is null ? null : new
    {
        reason = s.Reason, epoch = s.Epoch, threadId = s.ThreadId, breakpointId = s.BreakpointId, location = s.Location, source = s.Source,
        data = new { args = s.Args }, stackTop = s.StackTop,
    };

    /// <summary>Where vars/eval/set_value ran, so the agent always sees which frame it read.</summary>
    public static object Frame(SelectedFrame f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new { index = f.Index, threadId = f.ThreadId };
    }

    public static object FromOutcome(WaitOutcome outcome, WatchValue[]? watches = null) => outcome.Kind switch
    {
        WaitOutcomeKind.Break => new { state = "break", snapshot = Snapshot(outcome.Break!.Snapshot), watches },
        WaitOutcomeKind.Timeout => new { state = "running", hint = "The program has not stopped yet. Call debug_wait to keep waiting, or debug_pause." },
        WaitOutcomeKind.Ended => new { state = "design", message = "The debugging session ended." },
        _ => new { state = "disconnected", message = "The connection to Visual Studio was lost." },
    };

    /// <summary>The stop answer, with the agent's watch values when it has any (see <see cref="ReadWatchesAsync"/>).</summary>
    public static async Task<object> StopAnswerAsync(IConnectionManager connections, IDebugBridge bridge, WaitOutcome outcome, CallContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var watches = outcome.Kind == WaitOutcomeKind.Break ? await ReadWatchesAsync(connections, bridge, ctx, ct).ConfigureAwait(false) : null;
        return FromOutcome(outcome, watches);
    }

    /// <summary>
    /// The values of the agent's watch list now, or null: nothing is asked while the list is known to be empty or the extension is older than 2.9, a failing read
    /// never fails the answer it belongs to, and entries without any value (the program resumed meanwhile) are left out.
    /// </summary>
    public static async Task<WatchValue[]?> ReadWatchesAsync(IConnectionManager connections, IDebugBridge bridge, CallContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var session = connections.Session;
        if (session.WatchCount == 0 || connections.RemoteProtocolMinor < ProtocolInfo.MinorWatch) return null;
        try
        {
            var list = await bridge.ListWatchesAsync(ctx, ct).ConfigureAwait(false);
            session.SetWatchCount(list.Watches.Length);
            return list.Watches.Any(w => w.Value is not null || w.Error is not null) ? list.Watches : null;
        }
        catch (Exception e) when (e is RemoteInvocationException or ConnectionLostException or McpBridgeException)
        {
            return null;
        }
    }

    /// <summary>Gives the call a callId, logs its completion inside that scope, and turns expected failures into coded errors.</summary>
    public static async Task<CallToolResult> RunAsync(IConnectionManager connections, string tool, Func<CallContext, Task<CallToolResult>> body)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var context = CallIds.Next();
        var logger = connections.Logger;
        using var scope = Log.BeginCallScope(logger, context.CallId);
        var started = Stopwatch.GetTimestamp();
        var result = await RunCoreAsync(() => body(context)).ConfigureAwait(false);
        var durationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var code = ResultCode(result);
        Log.ToolCompleted(logger, tool, context.CallId, durationMs, code);
        return result;
    }

    private static async Task<CallToolResult> RunCoreAsync(Func<Task<CallToolResult>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (McpBridgeException e)
        {
            return Error(e.Code, e.Message, e.Hint);
        }
        catch (RemoteInvocationException e)
        {
            var (code, hint, reason) = BridgeErrors.Read(e);
            return Error(code, e.Message, hint, reason);
        }
        catch (ConnectionLostException e)
        {
            return Error(BridgeErrorCode.NotConnected, e.Message, "Call vs_instances, then vs_select.");
        }
    }

    private static string ResultCode(CallToolResult result)
    {
        if (result.IsError != true) return "ok";
        using var doc = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) ? c.GetString() ?? "error" : "error";
    }
}
