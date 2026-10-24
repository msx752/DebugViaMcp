using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

/// <summary>eval with offset and length: a long value read in pages.</summary>
public class EvalPagingToolTests
{
    private static InspectionTools Tools(FakeConnectionManager cm) => new(cm);

    [Fact]
    public async Task Offset_and_length_read_a_page_and_say_how_much_there_is()
    {
        var bridge = new FakeDebugBridge { OnReadValuePage = (e, o, l) => new ValuePage { Value = "abc", Type = "string", Offset = o, TotalLength = 9000, HasMore = true } };
        var cm = new FakeConnectionManager(bridge);

        var json = Json(await Tools(cm).Eval("note", false, 100, 3, CancellationToken.None));

        var data = json.GetProperty("data");
        Assert.Equal("abc", data.GetProperty("value").GetString());
        Assert.Equal(100, data.GetProperty("offset").GetInt32());
        Assert.Equal(9000, data.GetProperty("totalLength").GetInt32());
        Assert.True(data.GetProperty("hasMore").GetBoolean());
        Assert.Equal("string", data.GetProperty("type").GetString());
        Assert.Contains("ReadValuePage:note:100:3", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Only_a_length_starts_at_the_beginning_and_only_an_offset_uses_the_default_length()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        await Tools(cm).Eval("note", false, 0, 50, CancellationToken.None);
        await Tools(cm).Eval("note", false, 20, 0, CancellationToken.None);

        Assert.Contains("ReadValuePage:note:0:50", cm.Bridge.Calls);
        Assert.Contains($"ReadValuePage:note:20:{InspectionTools.DefaultPageLength}", cm.Bridge.Calls);
    }

    [Fact]
    public async Task A_length_above_the_most_is_cut_to_it()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        await Tools(cm).Eval("note", false, 0, 1_000_000, CancellationToken.None);

        Assert.Contains($"ReadValuePage:note:0:{InspectionTools.MaxPageLength}", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Without_offset_and_length_eval_is_the_old_eval()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await Tools(cm).Eval("n", false, 0, 0, CancellationToken.None));

        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("ReadValuePage", StringComparison.Ordinal));
        Assert.False(json.GetProperty("data").TryGetProperty("offset", out _));
    }

    [Fact]
    public async Task Paging_with_side_effects_is_refused_and_nothing_runs()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await Tools(cm).Eval("note = \"x\"", true, 0, 10, CancellationToken.None));

        Assert.Equal("invalid_argument", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("ReadValuePage", StringComparison.Ordinal));
        Assert.DoesNotContain("Evaluate", cm.Bridge.Calls);
    }

    [Fact]
    public async Task A_negative_offset_or_length_is_an_invalid_argument()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var offset = Json(await Tools(cm).Eval("note", false, -1, 10, CancellationToken.None));
        var length = Json(await Tools(cm).Eval("note", false, 0, -5, CancellationToken.None));

        Assert.Equal("invalid_argument", offset.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("invalid_argument", length.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_extension_below_2_9_is_asked_to_update_for_a_page()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { RemoteProtocolMinor = ProtocolInfo.MinorExceptionSettings };

        var json = Json(await Tools(cm).Eval("note", false, 0, 10, CancellationToken.None));

        Assert.Equal("unsupported", json.GetProperty("error").GetProperty("code").GetString());
    }
}
