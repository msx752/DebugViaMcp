namespace DebugViaMcp.Protocol;

public sealed class HandshakeRequest
{
    public string Token { get; set; } = "";
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public string ClientName { get; set; } = "";

    /// <summary>Protocol 2.2. Working directory of the agent's process; self-reported, used to choose and to approve the connection.</summary>
    public string? ClientWorkingDirectory { get; set; }
}

/// <summary>Protocol 2.2. A non-blocking hint about a tool call, such as an out-of-date build.</summary>
public sealed class BuildWarning
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Project { get; set; }
}

public static class BuildWarningCodes
{
    public const string StaleBuild = "stale_build";
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

    /// <summary>Protocol 2.2. Log retention in days from Visual Studio's Options; 0 when the extension did not say.</summary>
    public int LogRetentionDays { get; set; }

    /// <summary>Protocol 2.3. Longest a tool may wait for a break (debug_start, debug_continue, debug_step, debug_wait) in seconds,
    /// from Visual Studio's Options; 0 when the extension did not say (the server then uses its built-in limit).</summary>
    public int MaxWaitSeconds { get; set; }

    /// <summary>Protocol 2.3. Longest vs_build may wait in seconds, from Visual Studio's Options; 0 when the extension did not say.</summary>
    public int MaxBuildSeconds { get; set; }
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

    /// <summary>True when a secret-looking part of the value was replaced before it left Visual Studio; omitted otherwise.</summary>
    public bool? Masked { get; set; }

    /// <summary>Set by the MCP server when <c>vars</c> was asked for a depth above 1: the members read under this one. Never sent by the extension.</summary>
    public VariableInfo[]? Children { get; set; }

    /// <summary>True when <see cref="Children"/> holds only the first members; omitted otherwise.</summary>
    public bool? ChildrenTruncated { get; set; }
}

public sealed class BreakSnapshot
{
    public BreakReason Reason { get; set; }
    public string? BreakpointId { get; set; }
    public SourceLocation? Location { get; set; }
    public string[] Source { get; set; } = [];
    public VariableInfo[] Args { get; set; } = [];
    public string[] StackTop { get; set; } = [];
    public int? ThreadId { get; set; }

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

    /// <summary>Protocol 2.2. Client name of the agent that holds this Visual Studio; null when it is free.</summary>
    public string? LeaseHolder { get; set; }

    /// <summary>Protocol 2.7. Title of the dialog Visual Studio is waiting on (the agent cannot answer it); null when there is none. Untrusted text.</summary>
    public string? Dialog { get; set; }
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

    /// <summary>Protocol 2.6. The breakpoint removes itself after the break it causes. Only for a breakpoint this request creates.</summary>
    public bool Temporary { get; set; }
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

    /// <summary>True while the breakpoint is waiting to remove itself after its first break; omitted otherwise.</summary>
    public bool? Temporary { get; set; }
}

/// <summary>
/// The debugger options that decide whether symbols load and breakpoints bind (Tools → Options → Debugging → General), and the
/// breakpoints that are not bound. An option Visual Studio does not report is null.
/// </summary>
public sealed class SymbolStatus
{
    /// <summary>"Require source files to exactly match the original version".</summary>
    public bool? RequireSourceToMatch { get; set; }

    /// <summary>"Enable Just My Code".</summary>
    public bool? JustMyCode { get; set; }

    /// <summary>The automation property <c>WarnIfNoSymbols</c> of Debugging → General.</summary>
    public bool? WarnIfNoSymbols { get; set; }

    /// <summary>"Suppress JIT optimization on module load (Managed only)".</summary>
    public bool? SuppressJitOptimization { get; set; }

    /// <summary>"Enable Source Server support".</summary>
    public bool? SourceServer { get; set; }

    /// <summary>"Print Source Server diagnostic messages to the Output window".</summary>
    public bool? SourceServerDiagnostics { get; set; }

    /// <summary>Breakpoints that are not bound (in Design mode every breakpoint is pending), with the reason Visual Studio's state allows.</summary>
    public BreakpointInfo[] Unbound { get; set; } = [];

    /// <summary>What may keep a breakpoint unbound, given the options and the debugger mode; empty when every breakpoint is bound.</summary>
    public string[] Notes { get; set; } = [];
}

/// <summary>One entry of Debug → Windows → Exception Settings.</summary>
public sealed class ExceptionSettingInfo
{
    public string Name { get; set; } = "";
    public bool BreakWhenThrown { get; set; }
}

/// <summary>One group of Exception Settings (for example "Common Language Runtime Exceptions") with the entries that were asked for.</summary>
public sealed class ExceptionGroupInfo
{
    public string Name { get; set; } = "";
    public ExceptionSettingInfo[] Settings { get; set; } = [];

    /// <summary>Entries of the group that match the request, before the limit was applied.</summary>
    public int Total { get; set; }

    public bool Truncated { get; set; }
}

public sealed class ExceptionSettingsPage
{
    public ExceptionGroupInfo[] Groups { get; set; } = [];
}

/// <summary>Break When Thrown of one exception before and after a change.</summary>
public sealed class ExceptionChange
{
    /// <summary>The exception name; <c>group/name</c> in a reset result, which can span groups.</summary>
    public string Name { get; set; } = "";
    public bool Previous { get; set; }
    public bool Current { get; set; }

    /// <summary>False when the exception already had the requested value.</summary>
    public bool Changed { get; set; }
}

public sealed class ExceptionSetResult
{
    public string Group { get; set; } = "";
    public ExceptionChange[] Changes { get; set; } = [];
}

public sealed class ExceptionResetResult
{
    public ExceptionChange[] Restored { get; set; } = [];
}

/// <summary>One entry of an agent's watch list and its value at the current stop (protocol 2.9). Debuggee text: untrusted.</summary>
public sealed class WatchValue
{
    /// <summary>Stable inside the connection that added it.</summary>
    public string Id { get; set; } = "";
    public string Expression { get; set; } = "";

    /// <summary>The label the agent gave it; omitted when there is none.</summary>
    public string? Name { get; set; }

    /// <summary>Value text at this stop; omitted outside Break mode and when <see cref="Error"/> is set.</summary>
    public string? Value { get; set; }
    public string? Type { get; set; }

    /// <summary>True when the value text differs from the one at the previous stop that read it; false for the first value.</summary>
    public bool Changed { get; set; }

    /// <summary>Why this entry has no value (for example not in scope in this frame); one entry failing never fails the call.</summary>
    public string? Error { get; set; }
}

public sealed class AddWatchResult
{
    public WatchValue Watch { get; set; } = new();

    /// <summary>True when the same expression was already on the list; <see cref="Watch"/> is that entry.</summary>
    public bool AlreadyExisted { get; set; }
}

public sealed class WatchListResult
{
    public WatchValue[] Watches { get; set; } = [];
}

public sealed class RemoveWatchResult
{
    /// <summary>Ids that were removed.</summary>
    public string[] Removed { get; set; } = [];
}

/// <summary>
/// One page of a long value (protocol 2.9): a slice of the text Visual Studio shows for it (quotes and escapes included), cut after the whole
/// text was masked. Offsets are in that text; the pages of one value put together are the whole text.
/// </summary>
public sealed class ValuePage
{
    public string Value { get; set; } = "";
    public string? Type { get; set; }
    public int Offset { get; set; }

    /// <summary>Length of the whole text (after masking).</summary>
    public int TotalLength { get; set; }

    /// <summary>True when text follows this page.</summary>
    public bool HasMore { get; set; }

    /// <summary>True when a secret-looking part was replaced before the text was cut; omitted otherwise.</summary>
    public bool? Masked { get; set; }
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

public sealed class ThreadInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Location { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class StackInfo
{
    public long Epoch { get; set; }
    public int? ThreadId { get; set; }
    public StackFrameInfo[] Frames { get; set; } = [];
}

/// <summary>What frame_select selected: enough to continue without a second call.</summary>
public sealed class FrameSelection
{
    public int? ThreadId { get; set; }
    public int Index { get; set; }
    public long Epoch { get; set; }
    public SourceLocation? Location { get; set; }
    public string[] Source { get; set; } = [];
    public VariableInfo[] Args { get; set; } = [];
}

public sealed class InnerExceptionInfo
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>The exception the debugger stopped on ($exception). Message and stack trace are untrusted program data.</summary>
public sealed class ExceptionInfo
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public bool Truncated { get; set; }
    public SourceLocation? ThrowSite { get; set; }
    public InnerExceptionInfo[] Inner { get; set; } = [];

    /// <summary>True when a secret-looking part of the value was replaced before it left Visual Studio; omitted otherwise.</summary>
    public bool? Masked { get; set; }
}

public sealed class RunToResult
{
    /// <summary>Event sequence observed before resuming; wait for a break after it.</summary>
    public long Seq { get; set; }

    /// <summary>Set when the location cannot bind; nothing was resumed.</summary>
    public string? BindError { get; set; }
}

public sealed class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
}

public sealed class OutputPaneInfo
{
    public string Name { get; set; } = "";

    /// <summary>Null when Visual Studio has no text document for the pane yet (it is created when the pane is first shown).</summary>
    public int? Lines { get; set; }
}

/// <summary>A range of an Output pane, newest last. Lines are untrusted text written by tools and the program.</summary>
public sealed class OutputPage
{
    public string Pane { get; set; } = "";
    public int TotalLines { get; set; }

    /// <summary>1-based line number of Lines[0].</summary>
    public int FirstLine { get; set; }
    public string[] Lines { get; set; } = [];
    public bool Truncated { get; set; }
    public bool HasMore { get; set; }

    /// <summary>True when a secret-looking part of the value was replaced before it left Visual Studio; omitted otherwise.</summary>
    public bool? Masked { get; set; }

    /// <summary>Offset for the next (older) page.</summary>
    public int? NextOffset { get; set; }
}

/// <summary>One Error List entry. Message is untrusted text (analyzers and packages write it).</summary>
public sealed class ErrorItemInfo
{
    public ErrorSeverity Severity { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = "";
    public string? Project { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }

    /// <summary>"build" for build output, "live" for IntelliSense/analyzers.</summary>
    public string Origin { get; set; } = "";
}

public sealed class ErrorListPage
{
    public ErrorItemInfo[] Items { get; set; } = [];

    /// <summary>Totals over all entries, before the severity filter and paging.</summary>
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int Messages { get; set; }
    public bool HasMore { get; set; }
    public int? NextOffset { get; set; }
}

public sealed class BuildResult
{
    public BuildState State { get; set; }
    public bool Succeeded { get; set; }
    public int FailedProjects { get; set; }
    public long DurationMs { get; set; }
}

public sealed class EvaluateResult
{
    public bool IsValid { get; set; }
    public string Value { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Truncated { get; set; }

    /// <summary>True when a secret-looking part of the value was replaced before it left Visual Studio; omitted otherwise.</summary>
    public bool? Masked { get; set; }
}

/// <summary>What the user allows per tier (Tools → Options → Debugging → Debugging via MCP). Informational for the client; the VSIX enforces it.</summary>
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
