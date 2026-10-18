using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

public class RunToTests
{
    private static readonly object Owner = new();

    private static (DebugBridgeService Svc, FakeDte Dte) Create(DebuggerMode mode = DebuggerMode.Break)
    {
        var dte = new FakeDte { Mode = mode };
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    private static void HitLast(FakeDte dte) => dte.LastHitBreakpoint = dte.Bps[^1];

    [Fact]
    public async Task Adds_a_temporary_breakpoint_and_resumes()
    {
        var (svc, dte) = Create();

        var r = await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);

        Assert.Null(r.BindError);
        Assert.Single(dte.Bps);
        Assert.Contains("Go", dte.Calls);
        Assert.Empty(await svc.ListBreakpointsAsync(CancellationToken.None));   // never shown to the agent
    }

    [Fact]
    public async Task Stopping_there_reports_run_to_and_removes_the_breakpoint()
    {
        var (svc, dte) = Create();
        await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        HitLast(dte);

        var snapshot = svc.OnEnterBreak(BreakReason.Breakpoint);

        Assert.Equal(BreakReason.RunTo, snapshot.Reason);
        Assert.Empty(dte.Bps);
    }

    [Fact]
    public async Task Stopping_elsewhere_keeps_the_reason_and_still_removes_the_breakpoint()
    {
        var (svc, dte) = Create();
        await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);

        var snapshot = svc.OnEnterBreak(BreakReason.Exception);

        Assert.Equal(BreakReason.Exception, snapshot.Reason);
        Assert.Empty(dte.Bps);
    }

    /// <summary>Review focus #3.</summary>
    [Fact]
    public async Task Existing_user_breakpoint_is_used_and_kept()
    {
        var (svc, dte) = Create();
        await svc.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 12 }, CancellationToken.None);

        await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        HitLast(dte);
        var snapshot = svc.OnEnterBreak(BreakReason.Breakpoint);

        Assert.Equal(BreakReason.RunTo, snapshot.Reason);
        Assert.Single(dte.Bps);
        Assert.Single(await svc.ListBreakpointsAsync(CancellationToken.None));
    }

    /// <summary>Final review #1: a cancel or dropped connection during the bind wait must not leave a breakpoint in the solution.</summary>
    [Fact]
    public async Task Cancel_during_the_bind_wait_removes_the_owned_breakpoint()
    {
        var (svc, dte) = Create();
        dte.BoundCountFor = _ => 0;
        using var cts = new CancellationTokenSource();

        var run = svc.RunToAsync("OrderService.cs", 12, Owner, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(dte.Bps);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Fact]
    public async Task Connection_closing_during_the_bind_wait_removes_the_owned_breakpoint()
    {
        var (svc, dte) = Create();
        dte.BoundCountFor = _ => 0;

        var run = svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        await Task.Delay(100);
        await svc.ReleaseRunToAsync(Owner);                  // what Dispose does when the pipe closes
        await run;

        Assert.Empty(dte.Bps);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    /// <summary>Final review #2: reaching the target while still binding is the run-to; it must not be resumed past.</summary>
    [Fact]
    public async Task Run_mode_hit_during_the_bind_wait_is_the_run_to_and_does_not_resume()
    {
        var (svc, dte) = Create(DebuggerMode.Run);
        dte.BoundCountFor = _ => 0;

        var run = svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        await Task.Delay(100);
        dte.Bps[^1].BoundCount = 1;
        HitLast(dte);
        dte.Mode = DebuggerMode.Break;
        var snapshot = svc.OnEnterBreak(BreakReason.Breakpoint);
        var r = await run;

        Assert.Equal(BreakReason.RunTo, snapshot.Reason);
        Assert.Null(r.BindError);
        Assert.DoesNotContain("Go", dte.Calls);
        Assert.Empty(dte.Bps);
    }

    /// <summary>E2E: Visual Studio binds a new breakpoint asynchronously, after AddBreakpoint has returned.</summary>
    [Fact]
    public async Task Breakpoint_that_binds_shortly_after_being_added_is_used()
    {
        var (svc, dte) = Create();
        dte.BoundCountFor = _ => 0;

        var run = svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        await Task.Delay(150);
        if (dte.Bps.Count > 0) dte.Bps[^1].BoundCount = 1;
        var r = await run;

        Assert.Null(r.BindError);
        Assert.Contains("Go", dte.Calls);
    }

    [Fact]
    public async Task Unbindable_line_is_removed_and_nothing_resumes()
    {
        var (svc, dte) = Create();
        dte.BoundCountFor = _ => 0;

        var r = await svc.RunToAsync("OrderService.cs", 2, Owner, CancellationToken.None);

        Assert.NotNull(r.BindError);
        Assert.Empty(dte.Bps);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Fact]
    public async Task In_run_mode_it_only_arms_the_breakpoint()
    {
        var (svc, dte) = Create(DebuggerMode.Run);
        await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        Assert.Single(dte.Bps);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Fact]
    public async Task Session_end_and_disconnect_remove_the_breakpoint()
    {
        var (svc, dte) = Create();
        await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        svc.OnEnterDesign();
        Assert.Empty(dte.Bps);

        await svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None);
        await svc.ReleaseRunToAsync(new object());    // another connection: not its breakpoint
        Assert.Single(dte.Bps);
        await svc.ReleaseRunToAsync(Owner);
        Assert.Empty(dte.Bps);
    }

    [Fact]
    public async Task Design_mode_is_wrong_mode()
    {
        var (svc, _) = Create(DebuggerMode.Design);
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.RunToAsync("OrderService.cs", 12, Owner, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
    }
}
