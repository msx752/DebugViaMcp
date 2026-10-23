using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

/// <summary>One vars call opens a few levels of an object, so the agent does not need a call per level.</summary>
public class VarsExpansionTests
{
    private static VariableInfo V(string name, bool children = false, string value = "1") =>
        new() { Name = name, Type = "T", Value = value, HasChildren = children };

    /// <summary>locals: order → { Id, Lines → [ [0] → { Sku, Qty } ], Customer → { Name } }.</summary>
    private static FakeDebugBridge Tree(List<string?>? paths = null) => new()
    {
        OnVariables = (_, path, _) =>
        {
            paths?.Add(path);
            return path switch
            {
                null => [V("order", true), V("count")],
                "order" => [V("Id"), V("Lines", true), V("Customer", true)],
                "order.Lines" => [V("[0]", true)],
                "order.Lines[0]" => [V("Sku"), V("Qty")],
                "order.Customer" => [V("Name")],
                _ => throw new McpBridgeException(BridgeErrorCode.EvalFailed, "no such path " + path),
            };
        },
    };

    private static InspectionTools Tools(FakeDebugBridge bridge) => new(new FakeConnectionManager(bridge));

    private static System.Text.Json.JsonElement Item(System.Text.Json.JsonElement json, int i) => json.GetProperty("data")[i];

    [Fact]
    public async Task Depth_1_is_one_level_and_one_bridge_call()
    {
        var paths = new List<string?>();

        var json = Json(await Tools(Tree(paths)).Vars("locals", ct: CancellationToken.None));

        Assert.Equal([null], paths);
        Assert.False(Item(json, 0).TryGetProperty("children", out _));
    }

    [Fact]
    public async Task Depth_2_puts_the_members_of_each_object_under_it()
    {
        var json = Json(await Tools(Tree()).Vars("locals", depth: 2, ct: CancellationToken.None));

        var children = Item(json, 0).GetProperty("children");
        Assert.Equal(["Id", "Lines", "Customer"], children.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray());
        Assert.False(children[1].TryGetProperty("children", out _));   // one level further is the next depth
        Assert.False(Item(json, 1).TryGetProperty("children", out _)); // count has none
    }

    [Fact]
    public async Task Depth_3_reaches_an_element_and_builds_its_path_with_the_index()
    {
        var paths = new List<string?>();

        var json = Json(await Tools(Tree(paths)).Vars("locals", depth: 3, ct: CancellationToken.None));

        Assert.Contains("order.Lines", paths);
        var lines = Item(json, 0).GetProperty("children")[1].GetProperty("children");
        Assert.Equal("[0]", lines[0].GetProperty("name").GetString());
        Assert.DoesNotContain("order.Lines[0]", paths);   // depth 3 stops there
    }

    [Fact]
    public async Task Depth_4_reads_the_members_of_the_element()
    {
        var json = Json(await Tools(Tree()).Vars("locals", depth: 4, ct: CancellationToken.None));

        var element = Item(json, 0).GetProperty("children")[1].GetProperty("children")[0];
        Assert.Equal(["Sku", "Qty"], element.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray());
    }

    [Fact]
    public async Task Depth_is_capped_at_4()
    {
        var paths = new List<string?>();
        var bridge = new FakeDebugBridge { OnVariables = (_, path, _) => { paths.Add(path); return [V("n", true)]; } };

        await Tools(bridge).Vars("locals", depth: 99, ct: CancellationToken.None);

        Assert.Equal(4, paths.Count);   // the list itself and three deeper levels
    }

    [Fact]
    public async Task With_a_path_the_children_are_read_under_that_path()
    {
        var paths = new List<string?>();

        await Tools(Tree(paths)).Vars("locals", "order", depth: 2, ct: CancellationToken.None);

        Assert.Equal(["order", "order.Lines", "order.Customer"], paths);
    }

    [Fact]
    public async Task A_member_that_cannot_be_read_stays_closed_and_the_others_still_open()
    {
        var bridge = new FakeDebugBridge
        {
            OnVariables = (_, path, _) => path switch
            {
                null => [V("a", true), V("b", true)],
                "a" => throw new McpBridgeException(BridgeErrorCode.EvalFailed, "boom"),
                "b" => [V("x")],
                _ => [],
            },
        };

        var json = Json(await Tools(bridge).Vars("locals", depth: 2, ct: CancellationToken.None));

        Assert.False(Item(json, 0).TryGetProperty("children", out _));
        Assert.Equal("x", Item(json, 1).GetProperty("children")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_denied_read_is_not_swallowed()
    {
        var bridge = new FakeDebugBridge
        {
            OnVariables = (_, path, _) => path is null ? [V("a", true)] : throw new McpBridgeException(BridgeErrorCode.Denied, "no"),
        };

        var json = Json(await Tools(bridge).Vars("locals", depth: 2, ct: CancellationToken.None));

        Assert.Equal("denied", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Debugger_group_names_are_not_opened_as_paths()
    {
        var paths = new List<string?>();
        var bridge = new FakeDebugBridge { OnVariables = (_, path, _) => { paths.Add(path); return path is null ? [V("Non-Public members", true), V("Raw View", true)] : []; } };

        await Tools(bridge).Vars("locals", depth: 2, ct: CancellationToken.None);

        Assert.Equal([null], paths);
    }

    [Fact]
    public async Task A_long_member_list_is_cut_and_says_so()
    {
        var bridge = new FakeDebugBridge
        {
            OnVariables = (_, path, max) => path is null ? [V("big", true)] : Enumerable.Range(0, max).Select(i => V("m" + i)).ToArray(),
        };

        var json = Json(await Tools(bridge).Vars("locals", depth: 2, ct: CancellationToken.None));

        var big = Item(json, 0);
        Assert.Equal(VariableTree.ChildrenPerNode, big.GetProperty("children").GetArrayLength());
        Assert.True(big.GetProperty("childrenTruncated").GetBoolean());
    }

    [Fact]
    public async Task A_wide_tree_stops_at_the_node_limit()
    {
        var bridge = new FakeDebugBridge
        {
            OnVariables = (_, path, max) => Enumerable.Range(0, Math.Min(max, 30)).Select(i => V("n" + i, true)).ToArray(),
        };

        var json = Json(await Tools(bridge).Vars("locals", maxItems: 30, depth: 4, ct: CancellationToken.None));

        var nodes = CountNodes(json.GetProperty("data"));
        Assert.True(nodes <= VariableTree.MaxNodes + 30, $"{nodes} nodes");
        Assert.Contains("closed", json.GetProperty("hint").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static int CountNodes(System.Text.Json.JsonElement items) =>
        items.EnumerateArray().Sum(i => 1 + (i.TryGetProperty("children", out var c) ? CountNodes(c) : 0));

    [Fact]
    public async Task Expand_opens_named_paths_in_the_same_call()
    {
        var json = Json(await Tools(Tree()).Vars("locals", expand: ["order.Lines", "order.Customer", "order.Nope"], ct: CancellationToken.None));

        var expanded = json.GetProperty("expanded");
        Assert.Equal("order.Lines", expanded[0].GetProperty("path").GetString());
        Assert.Equal("[0]", expanded[0].GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("Name", expanded[1].GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Contains("no such path", expanded[2].GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expand_is_absent_when_not_asked_for()
    {
        var json = Json(await Tools(Tree()).Vars("locals", ct: CancellationToken.None));
        Assert.False(json.TryGetProperty("expanded", out _));
    }

    [Fact]
    public async Task Expand_is_limited_to_a_few_paths()
    {
        var paths = new List<string?>();
        var tools = Tools(Tree(paths));

        await tools.Vars("locals", expand: Enumerable.Range(0, 50).Select(i => "order.Customer").ToArray(), ct: CancellationToken.None);

        Assert.True(paths.Count(p => p == "order.Customer") <= VariableTree.MaxExpandPaths);
    }
}
