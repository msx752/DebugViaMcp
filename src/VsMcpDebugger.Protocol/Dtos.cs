namespace VsMcpDebugger.Protocol;

public sealed class HandshakeRequest
{
    public string Token { get; set; } = "";
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public string ClientName { get; set; } = "";
}

public sealed class HandshakeResponse
{
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public string VsVersion { get; set; } = "";
    public string VsixVersion { get; set; } = "";
    public string? SolutionPath { get; set; }
    public DebuggerMode Mode { get; set; }
    public long Seq { get; set; }
    public PermissionSet Permissions { get; set; } = new();
    public LogVerbosity LogLevel { get; set; } = LogVerbosity.Information;
}

public sealed class SourceLocation
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Function { get; set; } = "";
}

public sealed class VariableInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public bool HasChildren { get; set; }
    public bool Truncated { get; set; }
}

public sealed class BreakSnapshot
{
    public BreakReason Reason { get; set; }
    public string? BreakpointId { get; set; }
    public SourceLocation? Location { get; set; }
    public string[] Source { get; set; } = [];
    public VariableInfo[] Args { get; set; } = [];
    public string[] StackTop { get; set; } = [];

    /// <summary>Break counter of this VS session; frame indices are only valid within the same epoch.</summary>
    public long Epoch { get; set; }
}

public sealed class StatusInfo
{
    public DebuggerMode Mode { get; set; }
    public long Seq { get; set; }
    public BreakSnapshot? Snapshot { get; set; }
    public PermissionSet? Permissions { get; set; }
    public long Epoch { get; set; }
}

public sealed class BreakpointSpec
{
    public string? File { get; set; }
    public int? Line { get; set; }
    public string? Function { get; set; }
    public string? Condition { get; set; }
    public ConditionKind ConditionKind { get; set; }
    public int HitCount { get; set; }
    public HitCountKind HitCountKind { get; set; }
    public string? LogMessage { get; set; }
}

public sealed class BreakpointInfo
{
    public string Id { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public string? Function { get; set; }
    public string? Condition { get; set; }
    public ConditionKind ConditionKind { get; set; }
    public int HitCount { get; set; }
    public HitCountKind HitCountKind { get; set; }
    public string? LogMessage { get; set; }
    public bool Enabled { get; set; }
    public bool Bound { get; set; }
    public string? BindError { get; set; }
}

public sealed class SetBreakpointResult
{
    public BreakpointInfo Breakpoint { get; set; } = new();
    public bool AlreadyExisted { get; set; }
}

public sealed class StackFrameInfo
{
    public int Index { get; set; }
    public string Function { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public string Language { get; set; } = "";
}

public sealed class EvaluateResult
{
    public bool IsValid { get; set; }
    public string Value { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Truncated { get; set; }
}

/// <summary>What the user allows per tier (Tools → Options → VsMcpDebugger). Informational for the client; the VSIX enforces it.</summary>
public sealed class PermissionSet
{
    public PermissionLevel Read { get; set; } = PermissionLevel.Allow;
    public PermissionLevel Control { get; set; } = PermissionLevel.Allow;
    public PermissionLevel Mutate { get; set; } = PermissionLevel.Ask;
}

/// <summary>JSON-RPC <c>error.data</c> of every bridge error.</summary>
public sealed class BridgeErrorData
{
    public string? Hint { get; set; }

    /// <summary>Only for <see cref="BridgeErrorCode.Denied"/>; one of <see cref="DeniedReasons"/>.</summary>
    public string? Reason { get; set; }
}

public static class DeniedReasons
{
    public const string UserDenied = "user_denied";
    public const string Timeout = "timeout";
    public const string Policy = "policy";
    public const string Cancelled = "cancelled";
    public const string Disconnected = "disconnected";
}
