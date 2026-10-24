using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>watch_add, watch_list, watch_remove: an agent's own list, read at the stop it asks for, ended with the agent.</summary>
public class WatchTests
{
    private static (DebugBridgeService Svc, FakeDte Dte) Create()
    {
        var dte = new FakeDte();
        dte.OnEvaluate = e => new EvaluateResult { IsValid = true, Value = "1", Type = "int" };
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    private static async Task<WatchValue> AddAsync(DebugBridgeService svc, object owner, string expression, string? name = null) =>
        (await svc.AddWatchAsync(owner, expression, name, CancellationToken.None)).Watch;

    private static async Task<BridgeException> RefusedAsync(Func<Task> call) => await Assert.ThrowsAsync<BridgeException>(call);

    [Fact]
    public async Task Add_returns_the_entry_with_its_value_in_Break_mode()
    {
        var (svc, dte) = Create();
        dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = "12", Type = "decimal" };

        var added = await svc.AddWatchAsync(new object(), "order.Total", "total", CancellationToken.None);

        Assert.False(added.AlreadyExisted);
        Assert.Equal("w1", added.Watch.Id);
        Assert.Equal("order.Total", added.Watch.Expression);
        Assert.Equal("total", added.Watch.Name);
        Assert.Equal("12", added.Watch.Value);
        Assert.Equal("decimal", added.Watch.Type);
        Assert.False(added.Watch.Changed);
    }

    [Fact]
    public async Task A_long_value_is_cut_to_200_characters()
    {
        var (svc, dte) = Create();
        dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = new string('x', 3000), Type = "string" };

        var watch = await AddAsync(svc, new object(), "text");

        Assert.Equal(200, watch.Value!.Length);
    }

    [Fact]
    public async Task A_stop_with_a_list_prepares_nothing_again()
    {
        var (svc, dte) = Create();
        var owner = new object();
        await AddAsync(svc, owner, "i");
        var seen = new List<string>();
        dte.OnEvaluate = e => { seen.Add(e); return new EvaluateResult { IsValid = true, Value = "1", Type = "int" }; };

        await svc.ListWatchesAsync(owner, CancellationToken.None);
        await svc.ListWatchesAsync(owner, CancellationToken.None);

        Assert.Equal(["i, nse", "i, nse"], seen);
    }

    [Fact]
    public async Task Evaluation_is_side_effect_free()
    {
        var (svc, dte) = Create();

        await AddAsync(svc, new object(), "order.Total");

        Assert.EndsWith(", nse", dte.LastEvaluated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_outside_Break_mode_stores_the_entry_without_a_value()
    {
        var (svc, dte) = Create();
        dte.Mode = DebuggerMode.Design;

        var watch = await AddAsync(svc, new object(), "i");

        Assert.Equal("i", watch.Expression);
        Assert.Null(watch.Value);
        Assert.Null(dte.LastEvaluated);
    }

    [Fact]
    public async Task The_same_expression_again_is_the_same_entry()
    {
        var (svc, _) = Create();
        var owner = new object();
        var first = await AddAsync(svc, owner, "i");

        var again = await svc.AddWatchAsync(owner, "i", null, CancellationToken.None);

        Assert.True(again.AlreadyExisted);
        Assert.Equal(first.Id, again.Watch.Id);
        Assert.Single((await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches);
    }

    [Fact]
    public async Task The_eleventh_entry_is_refused_and_says_to_remove_one()
    {
        var (svc, _) = Create();
        var owner = new object();
        for (var i = 0; i < 10; i++) await AddAsync(svc, owner, "v" + i);

        var ex = await RefusedAsync(() => svc.AddWatchAsync(owner, "v10", null, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Contains("watch_remove", ex.Hint, StringComparison.Ordinal);
        Assert.Equal(10, (await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a; b")]
    [InlineData("x // comment")]
    public async Task A_blank_or_unsafe_expression_is_refused_and_not_stored(string expression)
    {
        var (svc, _) = Create();
        var owner = new object();

        var ex = await RefusedAsync(() => svc.AddWatchAsync(owner, expression, null, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Empty((await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches);
    }

    [Fact]
    public async Task An_expression_longer_than_500_characters_is_refused()
    {
        var (svc, _) = Create();

        var ex = await RefusedAsync(() => svc.AddWatchAsync(new object(), new string('a', 501), null, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task List_reads_every_entry_now_and_reports_what_changed_since_the_last_read()
    {
        var (svc, dte) = Create();
        var owner = new object();
        var n = 0;
        dte.OnEvaluate = e => new EvaluateResult { IsValid = true, Value = e == "i, nse" ? (++n > 2 ? "9" : "1") : "same", Type = "int" };
        await AddAsync(svc, owner, "i");     // first read: 1
        await AddAsync(svc, owner, "k");

        var second = (await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches;   // i read again: 1
        var third = (await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches;    // i now 9

        Assert.False(second[0].Changed);
        Assert.False(second[1].Changed);
        Assert.True(third[0].Changed);
        Assert.Equal("9", third[0].Value);
        Assert.False(third[1].Changed);
    }

    [Fact]
    public async Task An_entry_that_cannot_be_read_carries_its_error_and_the_others_still_come()
    {
        var (svc, dte) = Create();
        var owner = new object();
        dte.OnEvaluate = e => e.StartsWith("gone", StringComparison.Ordinal)
            ? new EvaluateResult { IsValid = false, Value = "The name 'gone' does not exist in the current context" }
            : new EvaluateResult { IsValid = true, Value = "5", Type = "int" };
        await AddAsync(svc, owner, "gone");
        await AddAsync(svc, owner, "i");

        var list = (await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches;

        Assert.Null(list[0].Value);
        Assert.Contains("does not exist", list[0].Error, StringComparison.Ordinal);
        Assert.Equal("5", list[1].Value);
        Assert.Null(list[1].Error);
    }

    [Fact]
    public async Task An_entry_whose_read_throws_carries_the_message_and_does_not_fail_the_call()
    {
        var (svc, dte) = Create();
        var owner = new object();
        dte.OnEvaluate = _ => throw new InvalidOperationException("evaluator gone");
        await AddAsync(svc, owner, "i");

        var list = (await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches;

        Assert.Contains("evaluator gone", Assert.Single(list).Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_that_looks_like_a_secret_is_masked()
    {
        var (svc, dte) = Create();
        dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = "\"hunter2-very-secret\"", Type = "string" };

        var watch = await AddAsync(svc, new object(), "password");

        Assert.DoesNotContain("hunter2", watch.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_outside_Break_mode_returns_the_entries_without_reading_anything()
    {
        var (svc, dte) = Create();
        var owner = new object();
        await AddAsync(svc, owner, "i");
        dte.Mode = DebuggerMode.Run;
        dte.LastEvaluated = null;

        var list = (await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches;

        Assert.Null(Assert.Single(list).Value);
        Assert.Null(dte.LastEvaluated);
    }

    [Fact]
    public async Task An_empty_list_reads_nothing()
    {
        var (svc, dte) = Create();

        var list = await svc.ListWatchesAsync(new object(), CancellationToken.None);

        Assert.Empty(list.Watches);
        Assert.Null(dte.LastEvaluated);
    }

    [Fact]
    public async Task Remove_by_id_and_all()
    {
        var (svc, _) = Create();
        var owner = new object();
        var a = await AddAsync(svc, owner, "a");
        await AddAsync(svc, owner, "b");
        await AddAsync(svc, owner, "c");

        var one = await svc.RemoveWatchAsync(owner, a.Id, false, CancellationToken.None);
        var rest = await svc.RemoveWatchAsync(owner, null, true, CancellationToken.None);

        Assert.Equal([a.Id], one.Removed);
        Assert.Equal(2, rest.Removed.Length);
        Assert.Empty((await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches);
    }

    [Fact]
    public async Task Remove_with_an_unknown_id_names_the_ids_that_exist()
    {
        var (svc, _) = Create();
        var owner = new object();
        var a = await AddAsync(svc, owner, "a");

        var ex = await RefusedAsync(() => svc.RemoveWatchAsync(owner, "w99", false, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
        Assert.Contains(a.Id, ex.Message + ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_without_an_id_or_all_is_an_invalid_argument()
    {
        var (svc, _) = Create();

        var ex = await RefusedAsync(() => svc.RemoveWatchAsync(new object(), null, false, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task Each_connection_has_its_own_list()
    {
        var (svc, _) = Create();
        var mine = new object();
        var other = new object();
        await AddAsync(svc, mine, "i");

        Assert.Empty((await svc.ListWatchesAsync(other, CancellationToken.None)).Watches);
        Assert.Equal("w1", (await AddAsync(svc, other, "k")).Id);
    }

    [Fact]
    public async Task Releasing_an_owner_drops_its_list()
    {
        var (svc, _) = Create();
        var owner = new object();
        await AddAsync(svc, owner, "i");

        await svc.ReleaseWatchesAsync(owner);

        Assert.Empty((await svc.ListWatchesAsync(owner, CancellationToken.None)).Watches);
    }
}
