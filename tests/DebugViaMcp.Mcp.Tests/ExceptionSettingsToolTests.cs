using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

public class ExceptionSettingsToolTests
{
    private const string Clr = "Common Language Runtime Exceptions";

    private static ExceptionSettingsTools Tools(FakeConnectionManager cm) => new(cm);

    [Fact]
    public async Task List_returns_the_groups_and_says_when_it_was_cut_short()
    {
        var bridge = new FakeDebugBridge
        {
            OnListExceptionSettings = (_, _, _, _) => new ExceptionSettingsPage
            {
                Groups = [new ExceptionGroupInfo { Name = Clr, Settings = [new ExceptionSettingInfo { Name = "System.A", BreakWhenThrown = true }], Total = 414, Truncated = true }],
            },
        };

        var json = Json(await Tools(new FakeConnectionManager(bridge)).ExceptionList(null, null, true, 1, CancellationToken.None));

        var group = json.GetProperty("groups")[0];
        Assert.Equal(Clr, group.GetProperty("name").GetString());
        Assert.Equal("System.A", group.GetProperty("settings")[0].GetProperty("name").GetString());
        Assert.True(group.GetProperty("settings")[0].GetProperty("breakWhenThrown").GetBoolean());
        Assert.Equal(414, group.GetProperty("total").GetInt32());
        Assert.Contains("filter", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_without_truncation_has_no_hint()
    {
        var json = Json(await Tools(new FakeConnectionManager(new FakeDebugBridge())).ExceptionList(null, null, true, 100, CancellationToken.None));

        Assert.False(json.TryGetProperty("hint", out _));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(10_000, 500)]
    [InlineData(50, 50)]
    public async Task List_clamps_the_limit(int asked, int sent)
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        await Tools(cm).ExceptionList(Clr, "Null", false, asked, CancellationToken.None);

        Assert.Contains($"ListExceptionSettings:{Clr}:Null:False:{sent}", cm.Bridge.Calls);
    }

    [Fact]
    public async Task List_treats_a_blank_group_and_filter_as_not_given()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        await Tools(cm).ExceptionList("  ", "", true, 100, CancellationToken.None);

        Assert.Contains("ListExceptionSettings:::True:100", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Set_returns_the_changes_and_points_to_reset()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await Tools(cm).ExceptionSet(Clr, ["System.NullReferenceException"], true, CancellationToken.None));

        Assert.Equal(Clr, json.GetProperty("group").GetString());
        var change = json.GetProperty("changes")[0];
        Assert.Equal("System.NullReferenceException", change.GetProperty("name").GetString());
        Assert.False(change.GetProperty("previous").GetBoolean());
        Assert.True(change.GetProperty("current").GetBoolean());
        Assert.True(change.GetProperty("changed").GetBoolean());
        Assert.Contains("exception_reset", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Contains($"SetExceptionSettings:{Clr}:System.NullReferenceException:True", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Set_with_nothing_changed_has_no_reset_hint()
    {
        var bridge = new FakeDebugBridge
        {
            OnSetExceptionSettings = (group, names, value) => new ExceptionSetResult
            {
                Group = group, Changes = [new ExceptionChange { Name = names[0], Previous = value, Current = value, Changed = false }],
            },
        };

        var json = Json(await Tools(new FakeConnectionManager(bridge)).ExceptionSet(Clr, ["System.A"], true, CancellationToken.None));

        Assert.False(json.TryGetProperty("hint", out _));
    }

    [Theory]
    [InlineData("", "System.A")]
    [InlineData("  ", "System.A")]
    [InlineData(Clr, null)]
    [InlineData(Clr, "")]
    [InlineData(Clr, " ")]
    public async Task Set_rejects_an_empty_group_or_name_before_calling_the_extension(string group, string? name)
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());

        var json = Json(await Tools(cm).ExceptionSet(group, name is null ? [] : [name], true, CancellationToken.None));

        Assert.Equal("invalid_argument", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("SetExceptionSettings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Set_rejects_more_than_fifty_names()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var names = Enumerable.Range(0, 51).Select(i => $"System.E{i}").ToArray();

        var json = Json(await Tools(cm).ExceptionSet(Clr, names, true, CancellationToken.None));

        Assert.Equal("invalid_argument", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("SetExceptionSettings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reset_returns_what_was_restored()
    {
        var bridge = new FakeDebugBridge
        {
            OnResetExceptionSettings = () => new ExceptionResetResult
            {
                Restored = [new ExceptionChange { Name = $"{Clr}/System.A", Previous = true, Current = false, Changed = true }],
            },
        };

        var json = Json(await Tools(new FakeConnectionManager(bridge)).ExceptionReset(CancellationToken.None));

        Assert.Equal($"{Clr}/System.A", json.GetProperty("restored")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_extension_without_exception_settings_is_unsupported_and_not_called()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { RemoteProtocolMinor = ProtocolInfo.MinorDialog };
        var tools = Tools(cm);

        foreach (var result in new[]
        {
            await tools.ExceptionList(null, null, true, 100, CancellationToken.None),
            await tools.ExceptionSet(Clr, ["System.A"], true, CancellationToken.None),
            await tools.ExceptionReset(CancellationToken.None),
        })
        {
            Assert.Equal("unsupported", Json(result).GetProperty("error").GetProperty("code").GetString());
        }

        Assert.DoesNotContain(cm.Bridge.Calls, c => c.Contains("ExceptionSettings", StringComparison.Ordinal));
    }
}
