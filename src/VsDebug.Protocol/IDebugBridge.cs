using System.Threading;
using System.Threading.Tasks;

namespace VsDebug.Protocol;

/// <summary>
/// Requests from the MCP host to the VSIX. Every call except <see cref="HandshakeAsync"/> requires a
/// successful handshake on the same connection. Execution calls return the event sequence number
/// observed immediately before the command was issued; the caller waits for a later break event.
/// </summary>
public interface IDebugBridge
{
    Task<HandshakeResponse> HandshakeAsync(HandshakeRequest request, CancellationToken cancellationToken);
    Task<StatusInfo> GetStatusAsync(CancellationToken cancellationToken);
    Task<BreakpointInfo[]> ListBreakpointsAsync(CancellationToken cancellationToken);
    Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CancellationToken cancellationToken);

    /// <returns>Number of breakpoints removed.</returns>
    Task<int> RemoveBreakpointsAsync(string? id, bool all, CancellationToken cancellationToken);

    Task<long> StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<long> ContinueAsync(CancellationToken cancellationToken);
    Task<long> PauseAsync(CancellationToken cancellationToken);
    Task<long> StepAsync(StepKind kind, CancellationToken cancellationToken);
    Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CancellationToken cancellationToken);
    Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int maxItems, CancellationToken cancellationToken);
    Task<EvaluateResult> EvaluateAsync(string expression, CancellationToken cancellationToken);
}
