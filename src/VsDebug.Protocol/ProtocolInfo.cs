namespace VsDebug.Protocol;

/// <summary>Wire-protocol version shared by the MCP host and the VSIX. A major mismatch is rejected at handshake.</summary>
public static class ProtocolInfo
{
    public const int Major = 1;
    public const int Minor = 0;
    public const string PipeNamePrefix = "vsdebug-";
}
