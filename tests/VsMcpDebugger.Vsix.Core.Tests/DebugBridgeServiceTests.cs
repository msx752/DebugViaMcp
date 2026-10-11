using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class DebugBridgeServiceTests
{
    private static (DebugBridgeService Svc, FakeDte Dte, EventSeq Seq) Create(DebuggerMode mode = DebuggerMode.Break)
    {
        var dte = new FakeDte { Mode = mode };
        var seq = new EventSeq();
        return (new DebugBridgeService(dte, new InlineUiThread(), seq, new NullLog(), "0.1.0"), dte, seq);
    }

    [Fact]
    public async Task SetBreakpoint_resolves_relative_path_and_is_idempotent()
    {
        var (svc, dte, _) = Create();
        var spec = new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "total > 1000" };

        var first = await svc.SetBreakpointAsync(spec, CancellationToken.None);
        var second = await svc.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "total > 1000" }, CancellationToken.None);

        Assert.Single(dte.Bps);
        Assert.Equal(@"C:\src\Demo\OrderService.cs", dte.Bps[0].File);
        Assert.Equal(first.Breakpoint.Id, second.Breakpoint.Id);
        Assert.True(second.AlreadyExisted);
        Assert.Equal("OrderService.cs", first.Breakpoint.File);
    }

    [Fact]
    public async Task Breakpoint_in_design_mode_reports_pending_bind()
    {
        var (svc, _, _) = Create(DebuggerMode.Design);

        var r = await svc.SetBreakpointAsync(new BreakpointSpec { Function = "Demo.OrderService.Submit" }, CancellationToken.None);

        Assert.False(r.Breakpoint.Bound);
        Assert.Contains("when debugging starts", r.Breakpoint.BindError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_file_is_invalid_argument()
    {
        var (svc, dte, _) = Create();
        dte.FileExistsFunc = _ => false;

        var ex = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetBreakpointAsync(new BreakpointSpec { File = "missing.cs", Line = 1 }, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Empty(dte.Bps);
    }

    [Theory]
    [InlineData(@"..\..\Windows\win.ini")]
    [InlineData(@"C:\Windows\win.ini")]
    public async Task Breakpoint_file_outside_solution_is_rejected_without_probing_disk(string file)
    {
        var (svc, dte, _) = Create();
        var probed = new List<string>();
        dte.FileExistsFunc = p => { probed.Add(p); return true; };

        var ex = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetBreakpointAsync(new BreakpointSpec { File = file, Line = 1 }, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Empty(probed);
        Assert.Empty(dte.Bps);
    }

    [Fact]
    public async Task Condition_with_side_effects_is_rejected()
    {
        var (svc, dte, _) = Create();

        var ex = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "order.Touch() > 0" }, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Empty(dte.Bps);
    }

    [Fact]
    public async Task Tracepoint_placeholder_with_side_effects_is_rejected()
    {
        var (svc, dte, _) = Create();

        var ex = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, LogMessage = "total={total} touched={order.Touch()}" }, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Empty(dte.Bps);
    }

    [Fact]
    public async Task Tracepoint_with_plain_placeholders_is_accepted()
    {
        var (svc, dte, _) = Create();

        await svc.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, LogMessage = "Order {order.Id} total={total}" }, CancellationToken.None);

        Assert.Single(dte.Bps);
    }

    [Fact]
    public async Task Remove_by_unknown_id_returns_zero()
    {
        var (svc, _, _) = Create();
        Assert.Equal(0, await svc.RemoveBreakpointsAsync("bp-00000000", all: false, CancellationToken.None));
    }

    [Fact]
    public async Task Step_returns_seq_observed_before_command()
    {
        var (svc, dte, seq) = Create();
        seq.Next();
        seq.Next();

        var observed = await svc.StepAsync(StepKind.Over, CancellationToken.None);

        Assert.Equal(2, observed);
        Assert.Equal("Step:Over", Assert.Single(dte.Calls));
    }

    [Fact]
    public async Task Step_in_run_mode_is_wrong_mode()
    {
        var (svc, _, _) = Create(DebuggerMode.Run);
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.StepAsync(StepKind.Over, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
    }

    [Fact]
    public async Task Variables_are_capped_at_max_items()
    {
        var (svc, dte, _) = Create();
        dte.Locals = Enumerable.Range(0, 10_000).Select(i => new VariableInfo { Name = "v" + i, Value = new string('x', 5000) }).ToList();

        var vars = await svc.GetVariablesAsync(VariableScope.Locals, null, 0, 50, CancellationToken.None);

        Assert.Equal(50, vars.Length);
        Assert.All(vars, v => Assert.True(v.Truncated && v.Value.Length == ValueShaper.MaxValueLength));
    }

    [Fact]
    public async Task Evaluate_applies_side_effect_policy_and_truncates()
    {
        var (svc, dte, _) = Create();

        var r = await svc.EvaluateAsync("order.Note", CancellationToken.None);

        Assert.Equal(ExpressionPolicy.PrepareSideEffectFree("order.Note"), dte.LastEvaluated);
        Assert.True(r.Truncated);
    }

    [Fact]
    public void Snapshot_contains_relative_location_and_stack_top()
    {
        var (svc, dte, _) = Create();
        dte.Frames.Add(new RawFrame { Function = "Program.Main", File = @"C:\src\Demo\Program.cs", Line = 3, Language = "C#" });

        var snap = svc.BuildSnapshot(BreakReason.Breakpoint);

        Assert.Equal("OrderService.cs", snap.Location!.File);
        Assert.Equal(9, snap.Location.Line);
        Assert.Equal(["Demo.OrderService.Submit(Demo.Order order):9", "Program.Main:3"], snap.StackTop);
    }

    [Fact]
    public async Task Variables_honour_offset()
    {
        var (svc, dte, _) = Create();
        dte.Locals = Enumerable.Range(0, 100).Select(i => new VariableInfo { Name = "v" + i, Value = "x" }).ToList();

        var vars = await svc.GetVariablesAsync(VariableScope.Locals, null, 40, 5, CancellationToken.None);

        Assert.Equal(["v40", "v41", "v42", "v43", "v44"], vars.Select(v => v.Name));
    }
}
