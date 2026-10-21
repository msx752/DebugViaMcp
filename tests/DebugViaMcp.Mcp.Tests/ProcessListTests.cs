using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

/// <summary>process_list shows what debug_attach can attach to, so the agent picks a pid without guessing a name.</summary>
public class ProcessListTests
{
    private static ProcessInfo[] Processes(params (int Pid, string Name)[] items) => items.Select(i => new ProcessInfo { Pid = i.Pid, Name = i.Name }).ToArray();

    [Fact]
    public async Task Lists_the_attachable_processes_with_pid_and_name()
    {
        var bridge = new FakeDebugBridge { OnListAttachable = _ => Processes((400, "OrderDemo"), (512, "FaultDemo")) };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromMilliseconds(50)).ProcessList(ct: CancellationToken.None));

        Assert.Equal(400, json.GetProperty("data")[0].GetProperty("pid").GetInt32());
        Assert.Equal("FaultDemo", json.GetProperty("data")[1].GetProperty("name").GetString());
        Assert.Equal(["ListAttachable:"], cm.Bridge.Calls);
    }

    [Fact]
    public async Task The_filter_matches_part_of_the_name_without_regard_to_case()
    {
        var bridge = new FakeDebugBridge { OnListAttachable = _ => Processes((1, "OrderDemo"), (2, "FaultDemo"), (3, "dotnet"), (4, "w3wp")) };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromMilliseconds(50)).ProcessList("DEMO", ct: CancellationToken.None));

        Assert.Equal([1, 2], json.GetProperty("data").EnumerateArray().Select(p => p.GetProperty("pid").GetInt32()).ToArray());
    }

    [Fact]
    public async Task A_long_list_is_cut_and_says_so()
    {
        var bridge = new FakeDebugBridge { OnListAttachable = _ => Enumerable.Range(1, 300).Select(i => new ProcessInfo { Pid = i, Name = "p" + i }).ToArray() };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromMilliseconds(50)).ProcessList(max: 100, ct: CancellationToken.None));

        Assert.Equal(100, json.GetProperty("data").GetArrayLength());
        Assert.True(json.GetProperty("hasMore").GetBoolean());
        Assert.Equal(300, json.GetProperty("total").GetInt32());
        Assert.Contains("filter", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_short_list_has_no_more_marker()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromMilliseconds(50)).ProcessList(ct: CancellationToken.None));

        Assert.False(json.TryGetProperty("hasMore", out _));
    }

    [Fact]
    public async Task An_empty_result_says_how_to_widen_it()
    {
        var bridge = new FakeDebugBridge { OnListAttachable = _ => [] };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromMilliseconds(50)).ProcessList("nothing", ct: CancellationToken.None));

        Assert.Equal(0, json.GetProperty("data").GetArrayLength());
        Assert.Contains("filter", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_extension_without_process_listing_is_unsupported()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design) { RemoteProtocolMinor = ProtocolInfo.MinorM3 - 1 };

        var json = Json(await new ExecutionTools(cm, TimeSpan.FromMilliseconds(50)).ProcessList(ct: CancellationToken.None));

        Assert.Equal("unsupported", json.GetProperty("error").GetProperty("code").GetString());
    }
}
