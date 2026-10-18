using System.Threading;

namespace DebugViaMcp.Vsix.Core.Bridge;

/// <summary>Monotonic sequence shared by all debugger events of one VS instance.</summary>
public sealed class EventSeq
{
    private long _value;

    public long Current => Interlocked.Read(ref _value);

    public long Next() => Interlocked.Increment(ref _value);
}
