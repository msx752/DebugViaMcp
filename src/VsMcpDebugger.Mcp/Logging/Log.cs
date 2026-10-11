using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace VsMcpDebugger.Mcp.Logging;

/// <summary>All Mcp log messages. Never pass variable values from the debuggee here.</summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipe to VS pid {Pid} disconnected: {Reason}")]
    public static partial void PipeDisconnected(ILogger logger, int pid, DisconnectedReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to VS pid {Pid} ({VsVersion}, VSIX {VsixVersion})")]
    public static partial void Connected(ILogger logger, int pid, string vsVersion, string vsixVersion);
}
