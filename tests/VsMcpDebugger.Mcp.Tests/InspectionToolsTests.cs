using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;
using Xunit;
using static VsMcpDebugger.Mcp.Tests.ExecutionToolsTests;

namespace VsMcpDebugger.Mcp.Tests;

public class InspectionToolsTests
{
    [Fact]
    public async Task Vars_requires_break_mode()
    {
        var tools = new InspectionTools(new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Run));
        var result = await tools.Vars("locals", null, 1, 50, ct: CancellationToken.None);
        Assert.Equal("wrong_mode", Json(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Vars_passes_path_and_clamps_max_items()
    {
        var bridge = new FakeDebugBridge();
        var seenMax = 0;
        string? seenPath = null;
        bridge.OnVariables = (_, path, max) => { seenPath = path; seenMax = max; return []; };
        var tools = new InspectionTools(new FakeConnectionManager(bridge));

        await tools.Vars("locals", "order.Lines", 1, 100_000, ct: CancellationToken.None);

        Assert.Equal("order.Lines", seenPath);
        Assert.Equal(InspectionTools.MaxItemsCap + 1, seenMax);   // one extra item tells us whether more exist
    }

    [Fact]
    public async Task Eval_failure_is_eval_failed_with_debugger_message()
    {
        var bridge = new FakeDebugBridge { OnEvaluate = _ => new EvaluateResult { IsValid = false, Value = "error CS0103: The name 'y' does not exist" } };
        var tools = new InspectionTools(new FakeConnectionManager(bridge));

        var error = Json(await tools.Eval("y", ct: CancellationToken.None)).GetProperty("error");

        Assert.Equal("eval_failed", error.GetProperty("code").GetString());
        Assert.Contains("CS0103", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eval_value_is_returned_in_data_field()
    {
        var tools = new InspectionTools(new FakeConnectionManager(new FakeDebugBridge()));
        var json = Json(await tools.Eval("x", ct: CancellationToken.None));
        Assert.Equal("42", json.GetProperty("data").GetProperty("value").GetString());
    }

    private static VariableInfo[] Items(int count, int valueLength) =>
        Enumerable.Range(0, count).Select(i => new VariableInfo { Name = "v" + i, Type = "string", Value = new string('x', valueLength) }).ToArray();

    /// <summary>Final review #5: a capped collection must say that more items exist and where to continue.</summary>
    [Fact]
    public async Task Vars_reports_has_more_and_next_offset()
    {
        var bridge = new FakeDebugBridge { OnVariables = (_, _, max) => Items(max, 3) };
        var tools = new InspectionTools(new FakeConnectionManager(bridge));

        var json = Json(await tools.Vars("locals", "list", 1, maxItems: 10, offset: 20, ct: CancellationToken.None));

        Assert.Equal(10, json.GetProperty("data").GetArrayLength());
        Assert.True(json.GetProperty("hasMore").GetBoolean());
        Assert.Equal(30, json.GetProperty("nextOffset").GetInt32());
    }

    [Fact]
    public async Task Vars_passes_offset_to_the_bridge()
    {
        var bridge = new FakeDebugBridge();
        var tools = new InspectionTools(new FakeConnectionManager(bridge));

        await tools.Vars("locals", "list", 1, maxItems: 10, offset: 40, ct: CancellationToken.None);

        Assert.Equal(40, bridge.LastOffset);
    }

    /// <summary>Final review #5 / spec §8: a vars response stays at or under 16 KB.</summary>
    [Fact]
    public async Task Vars_response_stays_under_16_kb()
    {
        var bridge = new FakeDebugBridge { OnVariables = (_, _, max) => Items(max, ValueShaper.MaxValueLength) };
        var tools = new InspectionTools(new FakeConnectionManager(bridge));

        var result = await tools.Vars("locals", "big", 1, maxItems: 200, ct: CancellationToken.None);
        var text = ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text;

        Assert.True(text.Length <= 16 * 1024, $"response was {text.Length} chars");
        Assert.True(Json(result).GetProperty("hasMore").GetBoolean());
    }
}
