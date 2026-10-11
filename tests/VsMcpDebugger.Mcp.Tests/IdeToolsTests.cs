using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;
using Xunit;
using static VsMcpDebugger.Mcp.Tests.ExecutionToolsTests;

namespace VsMcpDebugger.Mcp.Tests;

public class IdeToolsTests
{
    [Fact]
    public async Task Output_without_pane_lists_panes_in_any_mode()
    {
        var tools = new IdeTools(new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design));
        var json = Json(await tools.OutputRead(ct: CancellationToken.None));
        Assert.Equal("Build", json.GetProperty("panes")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Output_lines_are_data_and_tail_is_clamped()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Run);

        var json = Json(await new IdeTools(cm).OutputRead("build", tail: 100_000, ct: CancellationToken.None));

        Assert.Equal("b", json.GetProperty("data")[1].GetString());
        Assert.Contains("ReadOutput:build:500:0", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Errors_list_parses_severities_and_returns_totals_and_data()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);

        var json = Json(await new IdeTools(cm).ErrorsList("Error, warning", max: 1000, ct: CancellationToken.None));

        Assert.Equal([ErrorSeverity.Error, ErrorSeverity.Warning], cm.Bridge.LastSeverities);
        Assert.Contains("GetErrorList:0:200", cm.Bridge.Calls);
        Assert.Equal(1, json.GetProperty("totals").GetProperty("errors").GetInt32());
        Assert.Equal("CS0103", json.GetProperty("data")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("errors")]
    [InlineData("error,fatal")]
    public async Task Unknown_severity_is_invalid_argument(string severity)
    {
        var json = Json(await new IdeTools(new FakeConnectionManager(new FakeDebugBridge())).ErrorsList(severity, ct: CancellationToken.None));
        Assert.Equal("invalid_argument", json.GetProperty("error").GetProperty("code").GetString());
    }
}
