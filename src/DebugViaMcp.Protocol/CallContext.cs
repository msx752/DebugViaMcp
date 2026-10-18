namespace DebugViaMcp.Protocol;

/// <summary>Per-call metadata sent with every request after the handshake. CallId ties MCP, VSIX and audit log lines together.</summary>
public sealed class CallContext
{
    public string CallId { get; set; } = "";
}
