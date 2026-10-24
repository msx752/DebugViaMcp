namespace DebugViaMcp.Protocol;

/// <summary>
/// Wire-protocol version shared by the MCP host and the VSIX. A major mismatch is rejected at handshake; minors are
/// additive, and each side adapts to the other's minor (reported in the handshake).
/// </summary>
public static class ProtocolInfo
{
    public const int Major = 2;
    public const int Minor = 9;

    /// <summary>First minor with the M3 requests (threads, frame_select, exception, bp_enable, run_to, attach, output, error list, build) and frame events.</summary>
    public const int MinorM3 = 1;

    /// <summary>First minor with several-Visual-Studios support: working directory in the handshake, in_use, lease fields, vs_release, start-project and build-freshness requests.</summary>
    public const int MinorM31 = 2;

    /// <summary>First minor whose handshake carries the wait and build limits from Visual Studio's Options.</summary>
    public const int MinorTimeouts = 3;

    /// <summary>First minor whose variable, evaluate, exception and output results can carry <c>masked</c> (secret-looking values were hidden).</summary>
    public const int MinorMasking = 4;

    /// <summary>First minor with the <c>GetSymbolStatus</c> request (symbol-related debugger options and the breakpoints that are not bound).</summary>
    public const int MinorSymbols = 5;

    /// <summary>First minor whose breakpoint request understands <c>temporary</c> (an older extension would ignore it and keep the breakpoint).</summary>
    public const int MinorTemporary = 6;

    /// <summary>First minor whose status can name the dialog Visual Studio is waiting on, and whose calls that move the program fail with <c>busy</c> while one is open.</summary>
    public const int MinorDialog = 7;

    /// <summary>First minor with the <c>ListExceptionSettings</c>, <c>SetExceptionSettings</c> and <c>ResetExceptionSettings</c> requests (Break When Thrown per exception).</summary>
    public const int MinorExceptionSettings = 8;

    /// <summary>First minor with the <c>AddWatch</c>, <c>ListWatches</c> and <c>RemoveWatch</c> requests (an agent's watch list).</summary>
    public const int MinorWatch = 9;

    public const string PipeNamePrefix = "debugviamcp-";
}
