using VsDebug.Protocol;

namespace VsDebug.Mcp.Connection;

/// <summary>A bridge failure with a stable code and a next-step hint for the agent.</summary>
public sealed class McpBridgeException(BridgeErrorCode code, string message, string? hint = null, Exception? inner = null)
    : Exception(message, inner)
{
    public BridgeErrorCode Code { get; } = code;
    public string? Hint { get; } = hint;
}
