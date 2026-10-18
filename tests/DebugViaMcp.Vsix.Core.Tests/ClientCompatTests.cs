using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

public class ClientCompatTests
{
    private static BreakEvent RunTo() => new()
    {
        Seq = 4,
        Snapshot = new BreakSnapshot { Reason = BreakReason.RunTo, Location = new SourceLocation { File = "a.cs", Line = 3, Function = "A.M" } },
    };

    [Fact]
    public void Minor_0_client_sees_run_to_as_other()
    {
        var original = RunTo();

        var sent = ClientCompat.ForClient(original, clientMinor: 0);

        Assert.Equal(BreakReason.Other, sent.Snapshot.Reason);
        Assert.Equal(3, sent.Snapshot.Location!.Line);
        Assert.Equal(4, sent.Seq);
        Assert.Equal(BreakReason.RunTo, original.Snapshot.Reason);   // shared by all clients: never mutated
    }

    [Fact]
    public void Current_client_gets_the_same_event()
    {
        var e = RunTo();
        Assert.Same(e, ClientCompat.ForClient(e, ProtocolInfo.Minor));
    }

    [Fact]
    public void Other_reasons_are_never_copied()
    {
        var e = new BreakEvent { Seq = 1, Snapshot = new BreakSnapshot { Reason = BreakReason.Step } };
        Assert.Same(e, ClientCompat.ForClient(e, 0));
    }

    [Fact]
    public void Frame_events_go_only_to_m3_clients()
    {
        Assert.False(ClientCompat.ReceivesFrameEvents(0));
        Assert.True(ClientCompat.ReceivesFrameEvents(ProtocolInfo.MinorM3));
    }
}
