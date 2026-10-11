using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Mcp.Tests;

public class SessionStateTests
{
    private static BreakEvent Break(long seq) => new() { Seq = seq, Snapshot = new BreakSnapshot { Reason = BreakReason.Step } };

    private static SessionState Connected(DebuggerMode mode = DebuggerMode.Break, long seq = 5)
    {
        var s = new SessionState();
        s.Reset(mode, seq);
        return s;
    }

    [Fact]
    public async Task Frame_mirror_follows_frame_events_and_resets_on_break()
    {
        var s = Connected();
        Assert.Equal(new SelectedFrame(0, null), s.Frame);

        await s.OnFrameChangedAsync(new FrameChangedEvent { Seq = 6, Epoch = 1, ThreadId = 7, FrameIndex = 2 });
        Assert.Equal(new SelectedFrame(2, 7), s.Frame);
        Assert.Equal(6, s.LastSeq);

        await s.OnBreakAsync(new BreakEvent { Seq = 7, Snapshot = new BreakSnapshot { Reason = BreakReason.Step, ThreadId = 3 } });
        Assert.Equal(new SelectedFrame(0, 3), s.Frame);
    }

    [Fact]
    public async Task Break_published_before_wait_is_returned()
    {
        var s = Connected();
        await s.OnBreakAsync(Break(6));

        var outcome = await s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(WaitOutcomeKind.Break, outcome.Kind);
        Assert.Equal(6, outcome.Break!.Seq);
    }

    [Fact]
    public async Task Break_published_after_wait_starts_completes_it()
    {
        var s = Connected();
        var wait = s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(wait.IsCompleted);

        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Run, Seq = 6 });
        await s.OnBreakAsync(Break(7));

        var outcome = await wait;
        Assert.Equal(WaitOutcomeKind.Break, outcome.Kind);
        Assert.Equal(7, outcome.Break!.Seq);
        Assert.Equal(DebuggerMode.Break, s.Mode);
    }

    [Fact]
    public async Task Old_break_does_not_satisfy_newer_wait()
    {
        var s = Connected(seq: 5);
        await s.OnBreakAsync(Break(5));

        var outcome = await s.WaitForBreakAfterAsync(5, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.Equal(WaitOutcomeKind.Timeout, outcome.Kind);
    }

    [Fact]
    public async Task Mode_design_ends_wait()
    {
        var s = Connected(DebuggerMode.Run);
        var wait = s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(30), CancellationToken.None);

        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 6 });

        var outcome = await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(WaitOutcomeKind.Ended, outcome.Kind);
    }

    [Fact]
    public async Task Disconnect_ends_wait()
    {
        var s = Connected(DebuggerMode.Run);
        var wait = s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(30), CancellationToken.None);

        s.MarkDisconnected();

        var outcome = await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(WaitOutcomeKind.Disconnected, outcome.Kind);
        Assert.False(s.IsConnected);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var s = Connected(DebuggerMode.Run);
        using var cts = new CancellationTokenSource();
        var wait = s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(30), cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    /// <summary>Final review #3: the program can end before the waiter registers.</summary>
    [Fact]
    public async Task End_published_before_wait_is_returned()
    {
        var s = Connected(DebuggerMode.Run);
        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 6 });

        var outcome = await s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(5), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(WaitOutcomeKind.Ended, outcome.Kind);
    }

    [Fact]
    public async Task Later_end_wins_over_earlier_break()
    {
        var s = Connected(DebuggerMode.Run);
        await s.OnBreakAsync(Break(6));
        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 7 });

        var outcome = await s.WaitForBreakAfterAsync(5, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(WaitOutcomeKind.Ended, outcome.Kind);
    }

    /// <summary>E2E: a session that ended earlier must not answer a wait that starts after a new command.</summary>
    [Fact]
    public async Task Issued_command_moves_the_resume_point_past_an_earlier_session_end()
    {
        var s = Connected(DebuggerMode.Break, seq: 3);
        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 8 });   // previous session ended

        var seq = await s.IssueAsync(() => Task.FromResult(12L));                                    // e.g. debug_attach
        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Run, Seq = 13 });
        var waiting = s.WaitForBreakAfterAsync(s.LastResumeSeq, TimeSpan.FromSeconds(5), CancellationToken.None);
        await s.OnBreakAsync(Break(14));

        Assert.Equal(12, seq);
        Assert.Equal(WaitOutcomeKind.Break, (await waiting).Kind);
    }

    /// <summary>The user pressing F5 in Visual Studio is a resume too: a later wait must not return the old break.</summary>
    [Fact]
    public async Task Run_event_moves_the_resume_point_so_an_old_break_is_not_returned()
    {
        var s = Connected(DebuggerMode.Run, seq: 3);
        await s.OnBreakAsync(Break(5));
        await s.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Run, Seq = 6 });       // user resumed

        var outcome = await s.WaitForBreakAfterAsync(s.LastResumeSeq, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(WaitOutcomeKind.Timeout, outcome.Kind);
    }

    [Fact]
    public void Resume_seq_only_moves_forward()
    {
        var s = Connected(seq: 5);
        Assert.Equal(5, s.LastResumeSeq);
        s.MarkResumed(9);
        s.MarkResumed(7);
        Assert.Equal(9, s.LastResumeSeq);
    }
}
