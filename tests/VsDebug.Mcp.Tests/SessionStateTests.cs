using VsDebug.Mcp.Session;
using VsDebug.Protocol;
using Xunit;

namespace VsDebug.Mcp.Tests;

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
}
