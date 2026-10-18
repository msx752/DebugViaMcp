namespace DebugViaMcp.Mcp.Session;

/// <summary>The frame vars/eval/set_value run in, as last reported by the IDE (frame 0 of the stopping thread after each break).</summary>
public sealed record SelectedFrame(int Index, int? ThreadId);
