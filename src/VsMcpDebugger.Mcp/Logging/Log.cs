using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace VsMcpDebugger.Mcp.Logging;

/// <summary>All Mcp log messages. Never pass variable values from the debuggee here.</summary>
internal static partial class Log
{
    private static readonly Func<ILogger, string, IDisposable?> CallScope = LoggerMessage.DefineScope<string>("CallId {CallId}");

    /// <summary>Every log line written during a tool call carries its callId.</summary>
    public static IDisposable? BeginCallScope(ILogger logger, string callId) => CallScope(logger, callId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipe to VS pid {Pid} disconnected: {Reason}")]
    public static partial void PipeDisconnected(ILogger logger, int pid, DisconnectedReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to VS pid {Pid} ({VsVersion}, VSIX {VsixVersion})")]
    public static partial void Connected(ILogger logger, int pid, string vsVersion, string vsixVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool {Tool} [{CallId}] finished in {DurationMs} ms: {Result}")]
    public static partial void ToolCompleted(ILogger logger, string tool, string callId, long durationMs, string result);
}
