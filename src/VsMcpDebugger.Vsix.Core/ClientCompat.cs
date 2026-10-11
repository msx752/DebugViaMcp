using System;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>What a client of an older protocol minor can read. Applied per connection, just before sending.</summary>
public static class ClientCompat
{
    /// <summary>Events are shared by all clients, so a downgraded event is a copy; the original is never changed.</summary>
    public static BreakEvent ForClient(BreakEvent e, int clientMinor)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));
        if (clientMinor >= ProtocolInfo.MinorM3 || e.Snapshot.Reason != BreakReason.RunTo) return e;

        // A JSON copy keeps every field, including ones added in later minors.
        var copy = ProtocolJson.Deserialize<BreakEvent>(ProtocolJson.Serialize(e))!;
        copy.Snapshot.Reason = BreakReason.Other;
        return copy;
    }

    public static bool ReceivesFrameEvents(int clientMinor) => clientMinor >= ProtocolInfo.MinorM3;
}
