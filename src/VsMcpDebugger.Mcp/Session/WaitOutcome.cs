using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Session;

public enum WaitOutcomeKind { Break, Timeout, Ended, Disconnected }

public sealed record WaitOutcome(WaitOutcomeKind Kind, BreakEvent? Break);
