using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>eval with offset and length: a long value read in pages, masked as a whole before it is cut.</summary>
public class ValuePageTests
{
    private static string Literal(string text) => "\"" + text + "\"";

    private static (DebugBridgeService Svc, FakeDte Dte) Create(string text)
    {
        var dte = new FakeDte { OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = Literal(text), Type = "string" } };
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    [Fact]
    public async Task A_page_is_a_slice_of_the_displayed_text_with_the_total_and_whether_more_follows()
    {
        var text = string.Concat(Enumerable.Range(0, 3000).Select(i => (char)('a' + i % 26)));
        var (svc, _) = Create(text);
        var shown = Literal(text);

        var first = await svc.ReadValuePageAsync("note", 0, 1000, CancellationToken.None);
        var last = await svc.ReadValuePageAsync("note", 3000, 1000, CancellationToken.None);

        Assert.Equal(shown[..1000], first.Value);
        Assert.Equal(shown.Length, first.TotalLength);
        Assert.Equal(0, first.Offset);
        Assert.True(first.HasMore);
        Assert.Equal(shown[3000..], last.Value);
        Assert.False(last.HasMore);
        Assert.Equal("string", first.Type);
    }

    [Fact]
    public async Task Reading_every_page_gives_back_the_whole_text()
    {
        var text = new string('x', 2500) + "END";
        var (svc, _) = Create(text);
        var shown = Literal(text);
        var rebuilt = new System.Text.StringBuilder();

        for (var offset = 0; ; )
        {
            var page = await svc.ReadValuePageAsync("note", offset, 700, CancellationToken.None);
            rebuilt.Append(page.Value);
            if (!page.HasMore) break;
            offset += page.Value.Length;
        }

        Assert.Equal(shown, rebuilt.ToString());
    }

    [Fact]
    public async Task An_offset_past_the_end_is_an_empty_last_page()
    {
        var (svc, _) = Create("abc");

        var page = await svc.ReadValuePageAsync("note", 100, 10, CancellationToken.None);

        Assert.Equal("", page.Value);
        Assert.False(page.HasMore);
        Assert.Equal(5, page.TotalLength);
    }

    [Fact]
    public async Task The_expression_is_evaluated_once_and_without_side_effects()
    {
        var (svc, dte) = Create("abc");
        var calls = 0;
        dte.OnEvaluate = e => { calls++; return new EvaluateResult { IsValid = true, Value = "\"abc\"", Type = "string" }; };

        await svc.ReadValuePageAsync("note", 0, 10, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.EndsWith(", nse", dte.LastEvaluated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    [InlineData(0, 16385)]
    public async Task A_bad_offset_or_length_is_an_invalid_argument(int offset, int length)
    {
        var (svc, dte) = Create("abc");

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.ReadValuePageAsync("note", offset, length, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Null(dte.LastEvaluated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a; b")]
    public async Task A_blank_or_unsafe_expression_is_an_invalid_argument(string expression)
    {
        var (svc, _) = Create("abc");

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.ReadValuePageAsync(expression, 0, 10, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task An_expression_that_cannot_be_evaluated_is_an_eval_failure_with_the_message()
    {
        var (svc, dte) = Create("abc");
        dte.OnEvaluate = _ => new EvaluateResult { IsValid = false, Value = "The name 'nope' does not exist in the current context" };

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.ReadValuePageAsync("nope", 0, 10, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.EvalFailed, ex.Code);
        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_is_masked_in_the_whole_text_before_it_is_cut_so_no_page_boundary_hides_it()
    {
        var secret = "hunter2-very-secret-value";
        var (svc, dte) = Create(secret);
        dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = Literal(secret), Type = "string" };

        var all = string.Concat(await Task.WhenAll(Enumerable.Range(0, 4).Select(i => svc.ReadValuePageAsync("password", i * 8, 8, CancellationToken.None))).ContinueWith(t => t.Result.Select(p => p.Value)));
        var one = await svc.ReadValuePageAsync("password", 0, 8, CancellationToken.None);

        Assert.DoesNotContain("hunter2", all, StringComparison.Ordinal);
        Assert.True(one.Masked);
    }

    [Fact]
    public async Task A_value_longer_than_the_most_that_can_be_paged_is_refused_and_says_so()
    {
        // Masking is not what this is about (and it reads a text this long slowly under load): switch it off.
        var text = string.Concat(Enumerable.Repeat("lorem ipsum ", 175_000));
        var dte = new FakeDte { OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = Literal(text), Type = "string" } };
        var svc = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0", maskSensitiveValues: () => false);

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.ReadValuePageAsync("big", 0, 100, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Contains("2000000", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_needs_a_break()
    {
        var (svc, dte) = Create("abc");
        dte.Mode = DebuggerMode.Run;

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.ReadValuePageAsync("note", 0, 10, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
    }
}
