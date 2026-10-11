using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using StreamJsonRpc;
using VsMcpDebugger.Mcp.Connection;
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

    public static CallToolResult Error(BridgeErrorCode code, string message, string? hint = null) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = new { code = BridgeErrorNames.ToName(code), message, hint } }, Options) }],
    };

    public static CallToolResult WrongMode(DebuggerMode required, DebuggerMode current, string hint) =>
        Error(BridgeErrorCode.WrongMode, $"Requires {required} mode; current: {current}.", hint);

    public static object FromOutcome(WaitOutcome outcome) => outcome.Kind switch
    {
        WaitOutcomeKind.Break => new { state = "break", snapshot = outcome.Break!.Snapshot },
        WaitOutcomeKind.Timeout => new { state = "running", hint = "The program has not stopped yet. Call debug_wait to keep waiting, or debug_pause." },
        WaitOutcomeKind.Ended => new { state = "design", message = "The debugging session ended." },
        _ => new { state = "disconnected", message = "The connection to Visual Studio was lost." },
    };

    /// <summary>Runs a tool body and converts every expected failure into a coded error result.</summary>
    public static async Task<CallToolResult> RunAsync(Func<Task<CallToolResult>> body)
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
            var code = Enum.IsDefined(typeof(BridgeErrorCode), e.ErrorCode) ? (BridgeErrorCode)e.ErrorCode : BridgeErrorCode.Internal;
            return Error(code, e.Message, e.ErrorData?.ToString());
        }
        catch (ConnectionLostException e)
        {
            return Error(BridgeErrorCode.NotConnected, e.Message, "Call vs_instances, then vs_select.");
        }
    }
}
