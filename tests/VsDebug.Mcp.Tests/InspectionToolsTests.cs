using VsDebug.Mcp.Tests.Fakes;
using VsDebug.Mcp.Tools;
using VsDebug.Protocol;
using Xunit;
using static VsDebug.Mcp.Tests.ExecutionToolsTests;

namespace VsDebug.Mcp.Tests;

public class InspectionToolsTests
{
    [Fact]
    public async Task Vars_requires_break_mode()
    {
        var tools = new InspectionTools(new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Run));
        var result = await tools.Vars("locals", null, 1, 50, CancellationToken.None);
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

        await tools.Vars("locals", "order.Lines", 1, 100_000, CancellationToken.None);

        Assert.Equal("order.Lines", seenPath);
        Assert.Equal(InspectionTools.MaxItemsCap, seenMax);
    }

    [Fact]
    public async Task Eval_failure_is_eval_failed_with_debugger_message()
    {
        var bridge = new FakeDebugBridge { OnEvaluate = _ => new EvaluateResult { IsValid = false, Value = "error CS0103: The name 'y' does not exist" } };
        var tools = new InspectionTools(new FakeConnectionManager(bridge));

        var error = Json(await tools.Eval("y", CancellationToken.None)).GetProperty("error");

        Assert.Equal("eval_failed", error.GetProperty("code").GetString());
        Assert.Contains("CS0103", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eval_value_is_returned_in_data_field()
    {
        var tools = new InspectionTools(new FakeConnectionManager(new FakeDebugBridge()));
        var json = Json(await tools.Eval("x", CancellationToken.None));
        Assert.Equal("42", json.GetProperty("data").GetProperty("value").GetString());
    }
}
