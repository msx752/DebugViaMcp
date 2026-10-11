using System.Threading;
using System.Threading.Tasks;

namespace VsMcpDebugger.Protocol;

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

    Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CallContext context, CancellationToken cancellationToken);
    Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, CallContext context, CancellationToken cancellationToken);

    /// <summary>Assigns a value in the current frame (tier mutate). Returns the value read back after the assignment.</summary>
    Task<EvaluateResult> SetValueAsync(string expression, string value, CallContext context, CancellationToken cancellationToken);
}
