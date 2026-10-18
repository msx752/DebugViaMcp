namespace DebugViaMcp.Protocol;

/// <summary>
/// Wire-protocol version shared by the MCP host and the VSIX. A major mismatch is rejected at handshake; minors are
/// additive, and each side adapts to the other's minor (reported in the handshake).
/// </summary>
public static class ProtocolInfo
{
    public const int Major = 2;
    public const int Minor = 3;

    /// <summary>First minor with the M3 requests (threads, frame_select, exception, bp_enable, run_to, attach, output, error list, build) and frame events.</summary>
    public const int MinorM3 = 1;

    /// <summary>First minor with several-Visual-Studios support: working directory in the handshake, in_use, lease fields, vs_release, start-project and build-freshness requests.</summary>
    public const int MinorM31 = 2;

    /// <summary>First minor whose handshake carries the wait and build limits from Visual Studio's Options.</summary>
    public const int MinorTimeouts = 3;

    public const string PipeNamePrefix = "debugviamcp-";
}
