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

        var r = await svc.EvaluateAsync("order.Note", allowSideEffects: false, CancellationToken.None);

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

    /// <summary>Security review: the VSIX must enforce its own paging limits, not trust the client.</summary>
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(DebugBridgeService.MaxVariableOffset + 1)]
    public async Task Out_of_range_offset_is_rejected(int offset)
    {
        var (svc, _, _) = Create();
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.GetVariablesAsync(VariableScope.Locals, null, offset, 10, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task Page_size_is_capped_by_the_vsix()
    {
        var (svc, dte, _) = Create();
        dte.Locals = Enumerable.Range(0, 10_000).Select(i => new VariableInfo { Name = "v" + i, Value = "x" }).ToList();

        var vars = await svc.GetVariablesAsync(VariableScope.Locals, null, 0, 1_000_000, CancellationToken.None);

        Assert.Equal(DebugBridgeService.MaxVariablePage, vars.Length);
    }

    [Fact]
    public async Task SetValue_assigns_and_returns_the_new_value()
    {
        var (svc, dte, _) = Create();

        var r = await svc.SetValueAsync("total", "0", CancellationToken.None);

        Assert.Equal(("total", "0"), Assert.Single(dte.Assignments));
        Assert.True(r.IsValid);
        Assert.Equal("0", r.Value);
    }

    [Fact]
    public async Task SetValue_requires_break_mode()
    {
        var (svc, dte, _) = Create(DebuggerMode.Run);

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.SetValueAsync("total", "0", CancellationToken.None));

        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
        Assert.Empty(dte.Assignments);
    }

    [Theory]
    [InlineData("GetOrder().Total")]
    [InlineData("x = 5")]
    [InlineData("i++")]
    [InlineData("$\"{x}\"")]
    public async Task SetValue_target_must_be_assignable_without_side_effects(string target)
    {
        var (svc, dte, _) = Create();

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.SetValueAsync(target, "0", CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Contains("Target must be a variable, field, property or element", ex.Hint, StringComparison.Ordinal);
        Assert.Empty(dte.Assignments);
    }

    [Theory]
    [InlineData("order.Lines[2].Quantity")]
    [InlineData("total")]
    public async Task SetValue_accepts_members_and_elements(string target)
    {
        var (svc, dte, _) = Create();
        await svc.SetValueAsync(target, "1", CancellationToken.None);
        Assert.Single(dte.Assignments);
    }

    [Fact]
    public async Task SetValue_rejects_values_over_500_chars_and_multiline_values()
    {
        var (svc, dte, _) = Create();

        var tooLong = await Assert.ThrowsAsync<BridgeException>(() => svc.SetValueAsync("total", new string('1', 501), CancellationToken.None));
        var multiline = await Assert.ThrowsAsync<BridgeException>(() => svc.SetValueAsync("total", "0" + (char)10 + "x = 1", CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, tooLong.Code);
        Assert.Equal(BridgeErrorCode.InvalidArgument, multiline.Code);
        Assert.Empty(dte.Assignments);
    }

    [Fact]
    public async Task SetValue_reports_visual_studio_rejection_as_invalid_result()
    {
        var (svc, dte, _) = Create();
        dte.OnSetValue = (_, _) => new EvaluateResult { IsValid = false, Value = "Cannot convert 'abc' to decimal" };

        var r = await svc.SetValueAsync("total", "\"abc\"", CancellationToken.None);

        Assert.False(r.IsValid);
        Assert.Contains("Cannot convert", r.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_with_side_effects_does_not_append_nse()
    {
        var (svc, dte, _) = Create();

        await svc.EvaluateAsync("order.Touch()", allowSideEffects: true, CancellationToken.None);

        Assert.Equal("order.Touch()", dte.LastEvaluated);
    }

    [Fact]
    public async Task Evaluate_without_side_effects_still_appends_nse()
    {
        var (svc, dte, _) = Create();

        await svc.EvaluateAsync("order.Touch()", allowSideEffects: false, CancellationToken.None);

        Assert.Equal("order.Touch(), nse", dte.LastEvaluated);
    }

    [Fact]
    public async Task Evaluate_with_side_effects_is_limited_to_500_chars()
    {
        var (svc, _, _) = Create();
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.EvaluateAsync(new string('x', 501), allowSideEffects: true, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task Current_function_comes_from_the_top_frame()
    {
        var (svc, _, _) = Create();
        var (function, _) = await svc.GetBreakLocationAsync(CancellationToken.None);
        Assert.Equal("Demo.OrderService.Submit(Demo.Order order)", function);
    }

    [Fact]
    public async Task Every_break_starts_a_new_epoch_and_stamps_it_on_snapshot_and_status()
    {
        var (svc, _, _) = Create();

        var first = svc.OnEnterBreak(BreakReason.Breakpoint);
        var second = svc.OnEnterBreak(BreakReason.Step);
        var status = await svc.GetStatusAsync(CancellationToken.None);

        Assert.Equal(1, first.Epoch);
        Assert.Equal(2, second.Epoch);
        Assert.Equal(2, svc.Epoch);
        Assert.Equal(2, status.Epoch);
        Assert.Equal(2, status.Snapshot!.Epoch);
    }

    [Fact]
    public async Task Threads_mark_the_current_one()
    {
        var (svc, dte, _) = Create();
        dte.ThreadList = [new() { Id = 1, Name = "Main Thread" }, new() { Id = 7, Name = "Worker", Location = "Demo.Worker.Run" }];
        dte.CurrentThreadId = 7;

        var threads = await svc.GetThreadsAsync(CancellationToken.None);

        Assert.Equal([false, true], threads.Select(t => t.IsCurrent));
        Assert.Equal("Demo.Worker.Run", threads[1].Location);
    }

    [Fact]
    public async Task Thread_stack_reads_another_thread_and_carries_the_epoch()
    {
        var (svc, dte, _) = Create();
        dte.ThreadStacks[7] = [new() { Function = "Demo.Worker.Run", File = @"C:\src\Demo\Worker.cs", Line = 4, Language = "C#" }];
        svc.OnEnterBreak(BreakReason.Pause);

        var stack = await svc.GetThreadStackAsync(7, 20, CancellationToken.None);

        Assert.Equal(1, stack.Epoch);
        Assert.Equal(7, stack.ThreadId);
        Assert.Equal("Worker.cs", Assert.Single(stack.Frames).File);
    }

    [Fact]
    public async Task Thread_stack_without_thread_id_is_the_current_thread()
    {
        var (svc, _, _) = Create();
        var stack = await svc.GetThreadStackAsync(null, 20, CancellationToken.None);
        Assert.Equal(1, stack.ThreadId);
        Assert.Equal("OrderService.cs", Assert.Single(stack.Frames).File);
    }

    [Fact]
    public async Task Unknown_thread_is_not_found()
    {
        var (svc, _, _) = Create();
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.GetThreadStackAsync(99, 20, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public void Snapshot_names_the_stopping_thread()
    {
        var (svc, dte, _) = Create();
        dte.CurrentThreadId = 3;
        Assert.Equal(3, svc.OnEnterBreak(BreakReason.Step).ThreadId);
    }

    private static List<RawFrame> TwoFrames() =>
    [
        new() { Function = "Demo.OrderService.Calculate(Demo.Order order)", File = @"C:\src\Demo\OrderService.cs", Line = 22, Language = "C#" },
        new() { Function = "Demo.OrderService.Submit(Demo.Order order)", File = @"C:\src\Demo\OrderService.cs", Line = 7, Language = "C#" },
    ];

    [Fact]
    public async Task Frame_select_selects_in_visual_studio_and_describes_the_frame()
    {
        var (svc, dte, _) = Create();
        dte.Frames = TwoFrames();
        var epoch = svc.OnEnterBreak(BreakReason.Breakpoint).Epoch;

        var selection = await svc.SelectFrameAsync(1, epoch, null, CancellationToken.None);

        Assert.Equal(((int?)null, 1), Assert.Single(dte.Selections));
        Assert.Equal(1, selection.Index);
        Assert.Equal(epoch, selection.Epoch);
        Assert.Equal(7, selection.Location!.Line);
        Assert.Equal("Demo.OrderService.Submit(Demo.Order order)", selection.Location.Function);
    }

    /// <summary>Review focus #2.</summary>
    [Fact]
    public async Task Frame_select_with_an_old_epoch_is_stale_and_selects_nothing()
    {
        var (svc, dte, _) = Create();
        dte.Frames = TwoFrames();
        var old = svc.OnEnterBreak(BreakReason.Breakpoint).Epoch;
        svc.OnEnterBreak(BreakReason.Step);

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.SelectFrameAsync(1, old, null, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.Stale, ex.Code);
        Assert.Empty(dte.Selections);
    }

    [Fact]
    public async Task Frame_select_out_of_range_is_not_found()
    {
        var (svc, dte, _) = Create();
        var epoch = svc.OnEnterBreak(BreakReason.Breakpoint).Epoch;

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.SelectFrameAsync(5, epoch, null, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
        Assert.Empty(dte.Selections);
    }

    [Fact]
    public async Task Frame_select_on_another_thread_switches_the_thread()
    {
        var (svc, dte, _) = Create();
        dte.ThreadStacks[7] = TwoFrames();
        var epoch = svc.OnEnterBreak(BreakReason.Pause).Epoch;

        var selection = await svc.SelectFrameAsync(0, epoch, 7, CancellationToken.None);

        Assert.Equal(7, selection.ThreadId);
        Assert.Equal(7, dte.CurrentThreadId);
    }

    [Fact]
    public void Context_change_advances_the_sequence_and_reports_the_selection()
    {
        var (svc, dte, seq) = Create();
        dte.CurrentFrameIndex = 1;
        var before = seq.Current;

        var e = svc.OnContextChanged();

        Assert.Equal(before + 1, e.Seq);
        Assert.Equal(1, e.FrameIndex);
        Assert.Equal(1, e.ThreadId);
    }

    [Fact]
    public async Task Exception_reports_type_message_inner_chain_and_throw_site()
    {
        var (svc, dte, _) = Create();
        dte.ThrownException = new RawExceptionInfo
        {
            Type = "System.InvalidOperationException", Message = "Order 7 failed", StackTrace = new string('s', 10_000),
            Inner = Enumerable.Range(1, 8).Select(i => ("System.ArgumentException", "inner " + i)).ToList(),
        };

        var info = await svc.GetExceptionAsync(CancellationToken.None);

        Assert.Equal("System.InvalidOperationException", info!.Type);
        Assert.Equal("Order 7 failed", info.Message);
        Assert.Equal(DebugBridgeService.MaxInnerExceptions, info.Inner.Length);
        Assert.Equal("inner 1", info.Inner[0].Message);
        Assert.True(info.Truncated);
        Assert.Equal(DebugBridgeService.MaxStackTraceLength, info.StackTrace!.Length);
        Assert.Equal(9, info.ThrowSite!.Line);
    }

    [Fact]
    public async Task No_current_exception_is_null()
    {
        var (svc, _, _) = Create();
        Assert.Null(await svc.GetExceptionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Enable_toggles_by_id_and_unknown_id_is_null()
    {
        var (svc, dte, _) = Create();
        var set = await svc.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, CancellationToken.None);

        var off = await svc.SetBreakpointEnabledAsync(set.Breakpoint.Id, false, CancellationToken.None);
        var missing = await svc.SetBreakpointEnabledAsync("bp-00000000", false, CancellationToken.None);

        Assert.False(off!.Enabled);
        Assert.False(dte.Bps[0].Enabled);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Exception_requires_break_mode()
    {
        var (svc, _, _) = Create(DebuggerMode.Run);
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.GetExceptionAsync(CancellationToken.None));
        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
    }
}
