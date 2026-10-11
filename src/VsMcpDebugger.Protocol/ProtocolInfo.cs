namespace VsMcpDebugger.Protocol;

/// <summary>
/// Wire-protocol version shared by the MCP host and the VSIX. A major mismatch is rejected at handshake; minors are
/// additive, and each side adapts to the other's minor (reported in the handshake).
/// </summary>
public static class ProtocolInfo
{
    public const int Major = 2;
    public const int Minor = 1;

    /// <summary>First minor with the M3 requests (threads, frame_select, exception, bp_enable, run_to, attach, output, error list, build) and frame events.</summary>
    public const int MinorM3 = 1;

    public const string PipeNamePrefix = "vsmcpdebugger-";
}
