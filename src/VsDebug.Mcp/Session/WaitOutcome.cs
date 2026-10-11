using VsDebug.Protocol;

namespace VsDebug.Mcp.Session;

public enum WaitOutcomeKind { Break, Timeout, Ended, Disconnected }

public sealed record WaitOutcome(WaitOutcomeKind Kind, BreakEvent? Break);
