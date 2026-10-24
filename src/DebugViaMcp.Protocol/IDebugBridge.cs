using System.Threading;
using System.Threading.Tasks;

namespace DebugViaMcp.Protocol;

/// <summary>
/// Requests from the MCP host to the VSIX. Every call except <see cref="HandshakeAsync"/> requires a
/// successful handshake on the same connection. Execution calls return the event sequence number
/// observed immediately before the command was issued; the caller waits for a later break event.
/// </summary>
public interface IDebugBridge
{
    Task<HandshakeResponse> HandshakeAsync(HandshakeRequest request, CancellationToken cancellationToken);
    Task<StatusInfo> GetStatusAsync(CallContext context, CancellationToken cancellationToken);
    Task<BreakpointInfo[]> ListBreakpointsAsync(CallContext context, CancellationToken cancellationToken);
    Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CallContext context, CancellationToken cancellationToken);

    /// <returns>Number of breakpoints removed.</returns>
    Task<int> RemoveBreakpointsAsync(string? id, bool all, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. Enables or disables a breakpoint by id; null when no breakpoint has that id.</summary>
    Task<BreakpointInfo?> SetBreakpointEnabledAsync(string id, bool enabled, CallContext context, CancellationToken cancellationToken);

    Task<long> StartAsync(CallContext context, CancellationToken cancellationToken);
    /// <summary>Protocol 2.1. Processes of the current user that Visual Studio can attach to, optionally filtered by name.</summary>
    Task<ProcessInfo[]> ListAttachableAsync(string? processName, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. Attaches the debugger (Design mode, tier mutate). Returns the event sequence observed before attaching.</summary>
    Task<long> AttachAsync(int pid, CallContext context, CancellationToken cancellationToken);

    Task StopAsync(CallContext context, CancellationToken cancellationToken);
    Task<long> ContinueAsync(CallContext context, CancellationToken cancellationToken);
    /// <summary>Protocol 2.1. Runs to file:line through a temporary breakpoint (Break or Run mode).</summary>
    Task<RunToResult> RunToAsync(string file, int line, CallContext context, CancellationToken cancellationToken);

    Task<long> PauseAsync(CallContext context, CancellationToken cancellationToken);
    Task<long> StepAsync(StepKind kind, CallContext context, CancellationToken cancellationToken);
    Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CallContext context, CancellationToken cancellationToken);
    /// <summary>Protocol 2.1. Threads of the debugged program (Break mode).</summary>
    Task<ThreadInfo[]> GetThreadsAsync(CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. Stack of <paramref name="threadId"/> (default: the current thread) with the current epoch; does not change the selection.</summary>
    Task<StackInfo> GetThreadStackAsync(int? threadId, int maxFrames, CallContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Protocol 2.1. Selects frame <paramref name="index"/> of <paramref name="threadId"/> (default: current thread) in
    /// Visual Studio; vars/eval/set_value then run there. Fails with stale when <paramref name="epoch"/> is not current.
    /// </summary>
    Task<FrameSelection> SelectFrameAsync(int index, long epoch, int? threadId, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. The current exception in Break mode, or null when the break is not about one.</summary>
    Task<ExceptionInfo?> GetExceptionAsync(CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. Output window panes the agent may read (never the extension's own).</summary>
    Task<OutputPaneInfo[]> ListOutputPanesAsync(CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. The last <paramref name="tail"/> lines of a pane, skipping the newest <paramref name="offset"/> lines.</summary>
    Task<OutputPage> ReadOutputAsync(string pane, int tail, int offset, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.1. Error List entries as the Error List shows them (its scope filter applies), errors first.</summary>
    Task<ErrorListPage> GetErrorListAsync(ErrorSeverity[] severities, int offset, int max, CallContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Protocol 2.1. Builds the solution or <paramref name="project"/> (Design mode, tier control) and waits up to
    /// <paramref name="timeoutSec"/>; joins a build that is already running.
    /// </summary>
    Task<BuildResult> BuildAsync(string? project, int timeoutSec, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.2. Starts <paramref name="project"/> without changing the startup project (Design mode, tier control). Returns the event sequence observed before starting.</summary>
    Task<long> StartProjectAsync(string project, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.2. Out-of-date builds in scope: <paramref name="project"/>, else every project containing <paramref name="file"/>, else the startup project.</summary>
    Task<BuildWarning[]> CheckBuildFreshnessAsync(string? project, string? file, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.5. The symbol-related debugger options and the breakpoints that are not bound (tier read).</summary>
    Task<SymbolStatus> GetSymbolStatusAsync(CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.8. Break When Thrown per exception (tier read). Without <paramref name="group"/> every group is listed.</summary>
    Task<ExceptionSettingsPage> ListExceptionSettingsAsync(string? group, string? filter, bool onlyEnabled, int limit, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.8. Sets Break When Thrown of exact names in one group, all or none (tier control). The result carries each previous value.</summary>
    Task<ExceptionSetResult> SetExceptionSettingsAsync(string group, string[] names, bool breakWhenThrown, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.8. Restores what this connection changed with <see cref="SetExceptionSettingsAsync"/> (tier control).</summary>
    Task<ExceptionResetResult> ResetExceptionSettingsAsync(CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.9. Adds an expression to this connection's watch list (tier read). The expression must be side-effect free; the value comes at the next stop.</summary>
    Task<AddWatchResult> AddWatchAsync(string expression, string? name, CallContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Protocol 2.9. The list with each value read now in the selected frame; without values outside Break mode (tier read).
    /// <paramref name="atStop"/> is true when the server reads the values to put them in the answer of a stop: that read is covered by the
    /// question of <see cref="AddWatchAsync"/> and asks nothing more when Read is Ask (Deny still blocks it).
    /// </summary>
    Task<WatchListResult> ListWatchesAsync(bool atStop, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.9. Removes one entry by id, or all of them (tier read).</summary>
    Task<RemoveWatchResult> RemoveWatchAsync(string? id, bool all, CallContext context, CancellationToken cancellationToken);

    /// <summary>Protocol 2.2. Gives up this connection's hold on the Visual Studio so another agent can use it.</summary>
    Task ReleaseAsync(CallContext context, CancellationToken cancellationToken);

    Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CallContext context, CancellationToken cancellationToken);
    Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, CallContext context, CancellationToken cancellationToken);

    /// <summary>Assigns a value in the current frame (tier mutate). Returns the value read back after the assignment.</summary>
    Task<EvaluateResult> SetValueAsync(string expression, string value, CallContext context, CancellationToken cancellationToken);
}
