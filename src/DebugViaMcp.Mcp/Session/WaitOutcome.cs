using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Session;

public enum WaitOutcomeKind { Break, Timeout, Ended, Disconnected }

public sealed record WaitOutcome(WaitOutcomeKind Kind, BreakEvent? Break);
