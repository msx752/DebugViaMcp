namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Where the program was when a consent question was prepared: the event sequence and the top frame's position.
/// Visual Studio may raise no event when the user drags the instruction pointer (Set Next Statement), so the
/// approval is bound to the line as well as to the sequence.
/// </summary>
public sealed class ConsentPoint(long seq, string position)
{
    public long Seq { get; } = seq;
    public string Position { get; } = position;
}
