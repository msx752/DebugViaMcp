using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>bp_set with temporary: the breakpoint removes itself after the break it causes.</summary>
public class TemporaryBreakpointTests
{
    private static (DebugBridgeService Svc, FakeDte Dte) Create(DebuggerMode mode = DebuggerMode.Run)
    {
        var dte = new FakeDte { Mode = mode };
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    private static BreakpointSpec Spec(bool temporary, int line = 9) => new() { File = "OrderService.cs", Line = line, Temporary = temporary };

    [Fact]
    public async Task A_temporary_breakpoint_says_so_in_the_result_and_in_the_list()
    {
        var (svc, _) = Create();

        var set = await svc.SetBreakpointAsync(Spec(temporary: true), CancellationToken.None);
        var list = await svc.ListBreakpointsAsync(CancellationToken.None);

        Assert.True(set.Breakpoint.Temporary);
        Assert.True(Assert.Single(list).Temporary);
    }

    [Fact]
    public async Task An_ordinary_breakpoint_does_not_carry_the_flag()
    {
        var (svc, _) = Create();

        var set = await svc.SetBreakpointAsync(Spec(temporary: false), CancellationToken.None);

        Assert.Null(set.Breakpoint.Temporary);
    }

    [Fact]
    public async Task The_break_it_causes_removes_it()
    {
        var (svc, dte) = Create();
        await svc.SetBreakpointAsync(Spec(temporary: true), CancellationToken.None);
        dte.LastHitBreakpoint = dte.Bps[0];

        var snapshot = svc.OnEnterBreak(BreakReason.Breakpoint);

        Assert.Empty(dte.Bps);
        Assert.Equal(BreakReason.Breakpoint, snapshot.Reason);
        Assert.NotNull(snapshot.BreakpointId);   // the snapshot still says which breakpoint it was
        Assert.Empty(await svc.ListBreakpointsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_break_somewhere_else_leaves_it_in_place()
    {
        var (svc, dte) = Create();
        await svc.SetBreakpointAsync(Spec(temporary: true, line: 9), CancellationToken.None);
        await svc.SetBreakpointAsync(Spec(temporary: false, line: 20), CancellationToken.None);
        dte.LastHitBreakpoint = dte.Bps[1];

        svc.OnEnterBreak(BreakReason.Breakpoint);
        svc.OnEnterBreak(BreakReason.Step);

        Assert.Equal(2, dte.Bps.Count);
    }

    [Fact]
    public async Task An_ordinary_breakpoint_is_kept_after_its_break()
    {
        var (svc, dte) = Create();
        await svc.SetBreakpointAsync(Spec(temporary: false), CancellationToken.None);
        dte.LastHitBreakpoint = dte.Bps[0];

        svc.OnEnterBreak(BreakReason.Breakpoint);

        Assert.Single(dte.Bps);
    }

    [Fact]
    public async Task A_breakpoint_that_already_existed_is_not_made_temporary_so_the_users_own_stays()
    {
        var (svc, dte) = Create();
        await svc.SetBreakpointAsync(Spec(temporary: false), CancellationToken.None);

        var again = await svc.SetBreakpointAsync(Spec(temporary: true), CancellationToken.None);
        dte.LastHitBreakpoint = dte.Bps[0];
        svc.OnEnterBreak(BreakReason.Breakpoint);

        Assert.True(again.AlreadyExisted);
        Assert.Null(again.Breakpoint.Temporary);
        Assert.Single(dte.Bps);
    }

    [Fact]
    public async Task A_temporary_tracepoint_is_refused_because_it_never_stops()
    {
        var (svc, _) = Create();
        var spec = Spec(temporary: true);
        spec.LogMessage = "hit";

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.SetBreakpointAsync(spec, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task Removing_it_by_hand_forgets_it()
    {
        var (svc, dte) = Create();
        var set = await svc.SetBreakpointAsync(Spec(temporary: true), CancellationToken.None);
        await svc.RemoveBreakpointsAsync(set.Breakpoint.Id, all: false, CancellationToken.None);

        // The user sets the same breakpoint again as an ordinary one: it must not inherit the old flag.
        var again = await svc.SetBreakpointAsync(Spec(temporary: false), CancellationToken.None);
        dte.LastHitBreakpoint = dte.Bps[0];
        svc.OnEnterBreak(BreakReason.Breakpoint);

        Assert.Null(again.Breakpoint.Temporary);
        Assert.Single(dte.Bps);
    }
}
