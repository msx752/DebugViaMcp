using StreamJsonRpc;
using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

/// <summary>watch_add, watch_list, watch_remove and the values that come with a stop.</summary>
public class WatchToolTests
{
    private static WatchTools Tools(FakeConnectionManager cm) => new(cm);

    private static BreakEvent Break(long seq) => new()
    {
        Seq = seq,
        Snapshot = new BreakSnapshot { Reason = BreakReason.Step, Location = new SourceLocation { File = "OrderService.cs", Line = 9, Function = "OrderService.Submit" } },
    };

    private static WatchValue Value(string id, string expression, string? value, bool changed = false, string? error = null) =>
        new() { Id = id, Expression = expression, Value = value, Type = value is null ? null : "int", Changed = changed, Error = error };

    [Fact]
    public async Task Add_returns_the_entry_and_its_value()
    {
        var bridge = new FakeDebugBridge { OnAddWatch = (e, n) => new AddWatchResult { Watch = Value("w1", e, "12") } };
        var cm = new FakeConnectionManager(bridge);

        var json = Json(await Tools(cm).WatchAdd("order.Total", "total", CancellationToken.None));

        Assert.Equal("w1", json.GetProperty("watch").GetProperty("id").GetString());
        Assert.Equal("12", json.GetProperty("watch").GetProperty("value").GetString());
        Assert.False(json.TryGetProperty("alreadyExisted", out _));
        Assert.False(json.TryGetProperty("hint", out _));
        Assert.Contains("AddWatch:order.Total", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Add_outside_a_break_says_the_value_comes_at_the_next_stop()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);

        var json = Json(await Tools(cm).WatchAdd("i", null, CancellationToken.None));

        Assert.Contains("next stop", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_the_same_expression_again_says_so()
    {
        var bridge = new FakeDebugBridge { OnAddWatch = (e, _) => new AddWatchResult { Watch = Value("w1", e, "1"), AlreadyExisted = true } };

        var json = Json(await Tools(new FakeConnectionManager(bridge)).WatchAdd("i", null, CancellationToken.None));

        Assert.True(json.GetProperty("alreadyExisted").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_without_an_expression_is_an_invalid_argument_and_never_calls_the_bridge(string expression)
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await Tools(cm).WatchAdd(expression, null, CancellationToken.None));

        Assert.Equal("invalid_argument", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("AddWatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_extension_below_2_9_is_asked_to_update()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { RemoteProtocolMinor = ProtocolInfo.MinorExceptionSettings };

        var json = Json(await Tools(cm).WatchAdd("i", null, CancellationToken.None));

        Assert.Equal("unsupported", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("AddWatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_returns_every_entry()
    {
        var bridge = new FakeDebugBridge { OnListWatches = () => new WatchListResult { Watches = [Value("w1", "i", "3", changed: true), Value("w2", "gone", null, error: "not in scope")] } };

        var json = Json(await Tools(new FakeConnectionManager(bridge)).WatchList(CancellationToken.None));

        Assert.Equal(2, json.GetProperty("watches").GetArrayLength());
        Assert.True(json.GetProperty("watches")[0].GetProperty("changed").GetBoolean());
        Assert.Equal("not in scope", json.GetProperty("watches")[1].GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_explicit_watch_list_is_not_the_read_at_a_stop()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        await Tools(cm).WatchList(CancellationToken.None);

        Assert.Contains("ListWatches", cm.Bridge.Calls);
        Assert.DoesNotContain("ListWatches:atStop", cm.Bridge.Calls);
    }

    [Fact]
    public async Task An_empty_list_says_how_to_add_one()
    {
        var json = Json(await Tools(new FakeConnectionManager(new FakeDebugBridge())).WatchList(CancellationToken.None));

        Assert.Empty(json.GetProperty("watches").EnumerateArray());
        Assert.Contains("watch_add", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_passes_the_id_or_all_and_returns_what_was_removed()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var one = Json(await Tools(cm).WatchRemove("w1", false, CancellationToken.None));
        await Tools(cm).WatchRemove(null, true, CancellationToken.None);

        Assert.Equal("w1", one.GetProperty("removed")[0].GetString());
        Assert.Contains("RemoveWatch:w1", cm.Bridge.Calls);
        Assert.Contains("RemoveWatch:all", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Remove_without_an_id_or_all_is_an_invalid_argument()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await Tools(cm).WatchRemove(null, false, CancellationToken.None));

        Assert.Equal("invalid_argument", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("RemoveWatch", StringComparison.Ordinal));
    }

    // ---- values that come with a stop ----

    private static async Task<(System.Text.Json.JsonElement Json, FakeConnectionManager Cm)> StepAsync(FakeDebugBridge bridge, Action<FakeConnectionManager>? arrange = null)
    {
        var cm = new FakeConnectionManager(bridge);
        arrange?.Invoke(cm);
        bridge.OnStep = kind => { _ = Task.Run(() => cm.Session.OnBreakAsync(Break(11))); return 10; };
        var tools = new ExecutionTools(cm, TimeSpan.FromSeconds(5));
        return (Json(await tools.Step("over", CancellationToken.None)), cm);
    }

    [Fact]
    public async Task A_stop_carries_the_watch_values_when_the_list_is_not_empty()
    {
        var bridge = new FakeDebugBridge { OnListWatches = () => new WatchListResult { Watches = [Value("w1", "i", "3", changed: true)] } };

        var (json, cm) = await StepAsync(bridge, c => c.Session.SetWatchCount(1));

        Assert.Equal("break", json.GetProperty("state").GetString());
        Assert.Equal("3", json.GetProperty("watches")[0].GetProperty("value").GetString());
        Assert.True(json.GetProperty("watches")[0].GetProperty("changed").GetBoolean());
        Assert.Contains("ListWatches:atStop", cm.Bridge.Calls);   // a stop reads them as part of the answer, never as a question of its own
    }

    [Fact]
    public async Task A_stop_with_an_empty_list_asks_the_extension_for_nothing()
    {
        var (json, cm) = await StepAsync(new FakeDebugBridge());

        Assert.False(json.TryGetProperty("watches", out _));
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("ListWatches", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Watches_are_left_out_when_the_program_had_already_resumed()
    {
        var bridge = new FakeDebugBridge { OnListWatches = () => new WatchListResult { Watches = [Value("w1", "i", null)] } };

        var (json, _) = await StepAsync(bridge, c => c.Session.SetWatchCount(1));

        Assert.False(json.TryGetProperty("watches", out _));
    }

    [Fact]
    public async Task A_failing_watch_read_never_fails_the_stop_answer()
    {
        var bridge = new FakeDebugBridge { OnListWatches = () => throw new RemoteInvocationException("boom", (int)BridgeErrorCode.Internal, new BridgeErrorData()) };

        var (json, _) = await StepAsync(bridge, c => c.Session.SetWatchCount(1));

        Assert.Equal("break", json.GetProperty("state").GetString());
        Assert.False(json.TryGetProperty("watches", out _));
    }

    [Fact]
    public async Task An_extension_below_2_9_is_never_asked_for_watches()
    {
        var (json, cm) = await StepAsync(new FakeDebugBridge(), c => { c.Session.SetWatchCount(1); c.RemoteProtocolMinor = ProtocolInfo.MinorExceptionSettings; });

        Assert.False(json.TryGetProperty("watches", out _));
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("ListWatches", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_host_counts_what_add_and_remove_returned()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var tools = Tools(cm);

        await tools.WatchAdd("a", null, CancellationToken.None);
        await tools.WatchAdd("b", null, CancellationToken.None);
        Assert.Equal(2, cm.Session.WatchCount);

        await tools.WatchRemove("w1", false, CancellationToken.None);
        Assert.Equal(1, cm.Session.WatchCount);

        await tools.WatchRemove(null, true, CancellationToken.None);
        Assert.Equal(0, cm.Session.WatchCount);
    }

    [Fact]
    public async Task A_list_that_is_empty_resets_the_count()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Session.SetWatchCount(3);

        await Tools(cm).WatchList(CancellationToken.None);

        Assert.Equal(0, cm.Session.WatchCount);
    }

    [Fact]
    public async Task debug_wait_carries_the_values_too()
    {
        var bridge = new FakeDebugBridge { OnListWatches = () => new WatchListResult { Watches = [Value("w1", "i", "7")] } };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Run);
        cm.Session.SetWatchCount(1);
        _ = Task.Run(async () => { await Task.Delay(50); await cm.Session.OnBreakAsync(Break(11)); });

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromSeconds(5)).Wait(5, CancellationToken.None));

        Assert.Equal("7", json.GetProperty("watches")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task debug_status_in_Break_mode_carries_the_values()
    {
        var bridge = new FakeDebugBridge
        {
            OnStatus = () => new StatusInfo { Mode = DebuggerMode.Break, Snapshot = new BreakSnapshot { Reason = BreakReason.Other } },
            OnListWatches = () => new WatchListResult { Watches = [Value("w1", "i", "5")] },
        };
        var cm = new FakeConnectionManager(bridge);
        cm.Session.SetWatchCount(1);

        var json = Json(await new SessionTools(cm).Status(false, CancellationToken.None));

        Assert.Equal("5", json.GetProperty("watches")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task debug_status_outside_Break_mode_asks_for_nothing()
    {
        var bridge = new FakeDebugBridge { OnStatus = () => new StatusInfo { Mode = DebuggerMode.Design } };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);
        cm.Session.SetWatchCount(1);

        var json = Json(await new SessionTools(cm).Status(false, CancellationToken.None));

        Assert.False(json.TryGetProperty("watches", out _));
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("ListWatches", StringComparison.Ordinal));
    }
}
