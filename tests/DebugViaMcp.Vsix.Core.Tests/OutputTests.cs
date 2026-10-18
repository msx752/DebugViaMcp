using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

public class OutputTests
{
    private static IReadOnlyList<string> Lines(int first, int count) => Enumerable.Range(first, count).Select(n => "line " + n).ToList();

    private static DebugBridgeService Service(FakeDte dte) => new(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");

    [Fact]
    public void Tail_returns_the_newest_lines_and_where_to_continue()
    {
        var page = OutputPaging.Page("Build", 10, tail: 3, offset: 0, Lines);

        Assert.Equal(["line 8", "line 9", "line 10"], page.Lines);
        Assert.Equal(8, page.FirstLine);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.NextOffset);
    }

    [Fact]
    public void Offset_pages_towards_older_lines_and_stops_at_the_start()
    {
        var page = OutputPaging.Page("Build", 10, tail: 5, offset: 7, Lines);

        Assert.Equal(["line 1", "line 2", "line 3"], page.Lines);
        Assert.False(page.HasMore);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void Long_lines_are_truncated_and_the_page_stays_within_budget()
    {
        var page = OutputPaging.Page("Build", 500, tail: 500, offset: 0, (f, c) => Enumerable.Repeat(new string('x', 5000), c).ToList());

        Assert.True(page.Truncated);
        Assert.All(page.Lines, l => Assert.True(l.Length <= ValueShaper.MaxValueLength));
        Assert.True(page.Lines.Sum(l => l.Length) <= OutputPaging.BudgetChars);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void Empty_pane_is_an_empty_page()
    {
        var page = OutputPaging.Page("Debug", 0, 100, 0, (_, _) => throw new InvalidOperationException("must not read"));
        Assert.Empty(page.Lines);
        Assert.False(page.HasMore);
    }

    /// <summary>Review focus #4.</summary>
    [Fact]
    public async Task Reads_only_the_requested_range_from_visual_studio()
    {
        var dte = new FakeDte();
        dte.Panes[0].Lines = 100_000;

        var page = await Service(dte).ReadOutputAsync("build", 100, 0, CancellationToken.None);

        Assert.Equal((OutputPaneIds.Build, 99_901, 100), Assert.Single(dte.OutputReads));
        Assert.Equal("line 100000", page.Lines[^1]);
    }

    [Theory]
    [InlineData("Debugging via MCP Activity")]
    [InlineData("debugging via mcp activity")]
    public async Task The_extensions_own_panes_are_refused(string name)
    {
        var dte = new FakeDte();

        var ex = await Assert.ThrowsAsync<BridgeException>(() => Service(dte).ReadOutputAsync(name, 10, 0, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.Denied, ex.Code);
        Assert.Equal(DeniedReasons.Policy, ex.Reason);
        Assert.Empty(dte.OutputReads);
    }

    [Fact]
    public async Task Pane_list_hides_the_extensions_own_panes()
    {
        Assert.Equal(["Build"], (await Service(new FakeDte()).ListOutputPanesAsync(CancellationToken.None)).Select(p => p.Name));
    }

    /// <summary>E2E: a pane whose text document Visual Studio cannot give yet made the whole pane list fail.</summary>
    [Fact]
    public async Task Pane_without_a_readable_text_document_is_listed_with_unknown_size()
    {
        var dte = new FakeDte();
        dte.Panes.Add(new RawOutputPane { Id = Guid.NewGuid(), Name = "Package Manager", Lines = null });

        var panes = await Service(dte).ListOutputPanesAsync(CancellationToken.None);

        Assert.Equal(["Build", "Package Manager"], panes.Select(p => p.Name));
        Assert.Null(panes.Single(p => p.Name == "Package Manager").Lines);
        Assert.Equal(3, panes.Single(p => p.Name == "Build").Lines);
    }

    [Fact]
    public async Task Reading_a_pane_whose_text_is_unavailable_says_so_and_how_to_fix_it()
    {
        var dte = new FakeDte();
        dte.Panes.Add(new RawOutputPane { Id = Guid.NewGuid(), Name = "Package Manager", Lines = null });

        var ex = await Assert.ThrowsAsync<BridgeException>(() => Service(dte).ReadOutputAsync("Package Manager", 10, 0, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.Busy, ex.Code);
        Assert.Contains("Output window", ex.Hint, StringComparison.Ordinal);
        Assert.Empty(dte.OutputReads);
    }

    [Fact]
    public async Task Unknown_pane_is_not_found_with_the_pane_names()
    {
        var ex = await Assert.ThrowsAsync<BridgeException>(() => Service(new FakeDte()).ReadOutputAsync("Tests", 10, 0, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
        Assert.Contains("Build", ex.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denied_reason_reaches_the_client()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<StreamJsonRpc.LocalRpcException>(() => s.Handler.ReadOutputAsync("Debugging via MCP Activity", 10, 0, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal(DeniedReasons.Policy, ((BridgeErrorData)ex.ErrorData!).Reason);
    }
}
