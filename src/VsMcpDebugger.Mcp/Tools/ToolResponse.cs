using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using StreamJsonRpc;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Logging;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tools;

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
            "Update the VsMcpDebugger extension in Visual Studio to the same release as the vsmcpdebugger tool.");
    }

    public static CallToolResult WrongMode(DebuggerMode required, DebuggerMode current, string hint) =>
        Error(BridgeErrorCode.WrongMode, $"Requires {required} mode; current: {current}.", hint);

    /// <summary>Snapshot as the agent sees it: debuggee values under 'data' (spec §3.3).</summary>
    public static object? Snapshot(BreakSnapshot? s) => s is null ? null : new
    {
        reason = s.Reason, breakpointId = s.BreakpointId, location = s.Location, source = s.Source,
        data = new { args = s.Args }, stackTop = s.StackTop,
    };

    public static object FromOutcome(WaitOutcome outcome) => outcome.Kind switch
    {
        WaitOutcomeKind.Break => new { state = "break", snapshot = Snapshot(outcome.Break!.Snapshot) },
        WaitOutcomeKind.Timeout => new { state = "running", hint = "The program has not stopped yet. Call debug_wait to keep waiting, or debug_pause." },
        WaitOutcomeKind.Ended => new { state = "design", message = "The debugging session ended." },
        _ => new { state = "disconnected", message = "The connection to Visual Studio was lost." },
    };

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
