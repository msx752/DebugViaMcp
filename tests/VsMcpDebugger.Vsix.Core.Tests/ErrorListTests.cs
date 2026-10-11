using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class ErrorListTests
{
    private const string Dir = @"C:\src\Demo";

    private static RawErrorItem Item(ErrorSeverity severity, string file, int line, string code = "CS0001", bool build = true) => new()
    {
        Severity = severity, Code = code, Message = $"{code} at {line}", Project = "Demo", File = Dir + "\\" + file, Line = line, Column = 5, FromBuild = build,
    };

    private static readonly ErrorSeverity[] ErrorsAndWarnings = [ErrorSeverity.Error, ErrorSeverity.Warning];

    [Fact]
    public void Errors_come_first_then_file_and_line_with_relative_paths()
    {
        var items = new[] { Item(ErrorSeverity.Warning, "A.cs", 1), Item(ErrorSeverity.Error, "B.cs", 9), Item(ErrorSeverity.Error, "B.cs", 2), Item(ErrorSeverity.Error, "A.cs", 30) };

        var page = ErrorListShaping.Page(items, ErrorsAndWarnings, Dir, 0, 50);

        Assert.Equal(["A.cs:30", "B.cs:2", "B.cs:9", "A.cs:1"], page.Items.Select(i => $"{i.File}:{i.Line}"));
        Assert.Equal("build", page.Items[0].Origin);
    }

    [Fact]
    public void Totals_count_every_entry_before_filtering_and_paging()
    {
        var items = new[] { Item(ErrorSeverity.Error, "A.cs", 1), Item(ErrorSeverity.Warning, "A.cs", 2), Item(ErrorSeverity.Message, "A.cs", 3) };

        var page = ErrorListShaping.Page(items, [ErrorSeverity.Error], Dir, 0, 50);

        Assert.Single(page.Items);
        Assert.Equal((1, 1, 1), (page.Errors, page.Warnings, page.Messages));
    }

    [Fact]
    public void Paging_reports_where_to_continue()
    {
        var items = Enumerable.Range(1, 5).Select(n => Item(ErrorSeverity.Error, "A.cs", n)).ToArray();

        var page = ErrorListShaping.Page(items, ErrorsAndWarnings, Dir, 2, 2);

        Assert.Equal([3, 4], page.Items.Select(i => i.Line!.Value));
        Assert.True(page.HasMore);
        Assert.Equal(4, page.NextOffset);
    }

    [Fact]
    public void Long_messages_are_truncated_and_the_page_stays_within_budget()
    {
        var items = Enumerable.Range(1, 200).Select(n => Item(ErrorSeverity.Error, "A.cs", n)).ToArray();
        foreach (var i in items) i.Message = new string('m', 5000);

        var page = ErrorListShaping.Page(items, ErrorsAndWarnings, Dir, 0, 200);

        Assert.All(page.Items, i => Assert.True(i.Message.Length <= ValueShaper.MaxValueLength));
        Assert.True(page.Items.Sum(i => i.Message.Length) <= OutputPaging.BudgetChars);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Service_reads_the_error_list_in_any_mode()
    {
        var dte = new FakeDte { Mode = DebuggerMode.Design, Errors = [Item(ErrorSeverity.Error, "A.cs", 1, build: false)] };
        var svc = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");

        var page = await svc.GetErrorListAsync(ErrorsAndWarnings, 0, 50, CancellationToken.None);

        Assert.Equal("live", Assert.Single(page.Items).Origin);
    }
}
