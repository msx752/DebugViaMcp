using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>symbol_status: the symbol-related debugger options, and which breakpoints are not bound and why that may be.</summary>
public class SymbolStatusTests
{
    private static (DebugBridgeService Svc, FakeDte Dte) Create(DebuggerMode mode = DebuggerMode.Break)
    {
        var dte = new FakeDte { Mode = mode };
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    private static RawBreakpoint Bp(int line, int bound) => new() { File = @"C:\src\Demo\OrderService.cs", Line = line, Enabled = true, BoundCount = bound };

    [Fact]
    public async Task The_options_come_from_the_debugger_settings()
    {
        var (svc, dte) = Create();
        dte.Options = new RawDebuggerOptions { RequireSourceToMatch = true, JustMyCode = false, WarnIfNoSymbols = true, SuppressJitOptimization = false, SourceServer = false, SourceServerDiagnostics = true };

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.True(s.RequireSourceToMatch);
        Assert.False(s.JustMyCode);
        Assert.True(s.WarnIfNoSymbols);
        Assert.False(s.SuppressJitOptimization);
        Assert.False(s.SourceServer);
        Assert.True(s.SourceServerDiagnostics);
    }

    [Fact]
    public async Task An_option_Visual_Studio_does_not_have_stays_unknown()
    {
        var (svc, dte) = Create();
        dte.Options = new RawDebuggerOptions { JustMyCode = true };

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.Null(s.RequireSourceToMatch);
        Assert.True(s.JustMyCode);
    }

    [Fact]
    public async Task Bound_breakpoints_are_not_listed_and_nothing_needs_explaining()
    {
        var (svc, dte) = Create();
        dte.Bps.Add(Bp(9, bound: 1));

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.Empty(s.Unbound);
        Assert.Empty(s.Notes);
    }

    [Fact]
    public async Task A_breakpoint_that_is_not_bound_is_listed_with_its_reason()
    {
        var (svc, dte) = Create();
        dte.Bps.Add(Bp(9, bound: 1));
        dte.Bps.Add(Bp(14, bound: 0));

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        var unbound = Assert.Single(s.Unbound);
        Assert.Equal(14, unbound.Line);
        Assert.False(unbound.Bound);
        Assert.Contains("symbols", unbound.BindError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task In_design_mode_a_pending_breakpoint_is_explained_by_the_mode()
    {
        var (svc, dte) = Create(DebuggerMode.Design);
        dte.Bps.Add(Bp(9, bound: 0));

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.Contains(s.Notes, n => n.Contains("when debugging starts", StringComparison.Ordinal));
        Assert.DoesNotContain(s.Notes, n => n.Contains("Modules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task While_debugging_an_unbound_breakpoint_points_to_the_modules_window()
    {
        var (svc, dte) = Create(DebuggerMode.Run);
        dte.Bps.Add(Bp(9, bound: 0));

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.Contains(s.Notes, n => n.Contains("Modules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_options_that_can_keep_a_breakpoint_unbound_are_named_only_when_one_is()
    {
        var (svc, dte) = Create();
        dte.Options = new RawDebuggerOptions { RequireSourceToMatch = true, JustMyCode = true };
        dte.Bps.Add(Bp(9, bound: 0));

        var withUnbound = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.Contains(withUnbound.Notes, n => n.Contains("exactly match", StringComparison.Ordinal));
        Assert.Contains(withUnbound.Notes, n => n.Contains("Just My Code", StringComparison.Ordinal));

        dte.Bps.Clear();
        var without = await svc.GetSymbolStatusAsync(CancellationToken.None);
        Assert.Empty(without.Notes);
    }

    [Fact]
    public async Task Options_that_are_off_add_no_note()
    {
        var (svc, dte) = Create();
        dte.Options = new RawDebuggerOptions { RequireSourceToMatch = false, JustMyCode = false };
        dte.Bps.Add(Bp(9, bound: 0));

        var s = await svc.GetSymbolStatusAsync(CancellationToken.None);

        Assert.DoesNotContain(s.Notes, n => n.Contains("exactly match", StringComparison.Ordinal) || n.Contains("Just My Code", StringComparison.Ordinal));
    }
}
