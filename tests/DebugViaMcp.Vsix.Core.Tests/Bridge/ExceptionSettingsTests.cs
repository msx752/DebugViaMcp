using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>exception_list, exception_set and exception_reset: Break When Thrown per exception, all or nothing, and undoable.</summary>
public class ExceptionSettingsTests
{
    private const string Clr = "Common Language Runtime Exceptions";

    private static (DebugBridgeService Svc, FakeDte Dte) Create()
    {
        var dte = new FakeDte();
        dte.ExceptionGroupsData.Add(Group(Clr,
            ("System.ArgumentException", false), ("System.ArgumentNullException", false), ("System.InvalidOperationException", true),
            ("System.IO.IOException", false), ("System.NullReferenceException", true), ("System.TimeoutException", false),
            ("System.Net.Http.HttpRequestException", false), ("System.OperationCanceledException", false)));
        dte.ExceptionGroupsData.Add(Group("C++ Exceptions", ("std::bad_alloc", false), ("std::exception", false)));
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    private static RawExceptionGroup Group(string name, params (string Name, bool On)[] settings) => new()
    {
        Name = name,
        Settings = settings.Select(s => new RawExceptionSetting { Name = s.Name, BreakWhenThrown = s.On }).ToList(),
    };

    private static bool Value(FakeDte dte, string group, string name) =>
        dte.ExceptionGroupsData.Single(g => g.Name == group).Settings.Single(s => s.Name == name).BreakWhenThrown;

    [Fact]
    public async Task List_returns_only_enabled_by_default_with_total_and_not_truncated()
    {
        var (svc, _) = Create();

        var page = await svc.ListExceptionSettingsAsync(Clr, null, true, 100, CancellationToken.None);

        var group = Assert.Single(page.Groups);
        Assert.Equal(["System.InvalidOperationException", "System.NullReferenceException"], group.Settings.Select(s => s.Name));
        Assert.All(group.Settings, s => Assert.True(s.BreakWhenThrown));
        Assert.Equal(2, group.Total);
        Assert.False(group.Truncated);
    }

    [Fact]
    public async Task List_without_a_group_skips_groups_with_no_match()
    {
        var (svc, _) = Create();

        var page = await svc.ListExceptionSettingsAsync(null, null, true, 100, CancellationToken.None);

        Assert.Equal([Clr], page.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task List_filter_is_a_case_insensitive_substring_and_limit_sets_truncated()
    {
        var (svc, _) = Create();

        var page = await svc.ListExceptionSettingsAsync(Clr, "EXCEPTION", false, 3, CancellationToken.None);

        var group = Assert.Single(page.Groups);
        Assert.Equal(3, group.Settings.Length);
        Assert.Equal(8, group.Total);
        Assert.True(group.Truncated);
    }

    [Fact]
    public async Task List_limit_is_shared_by_all_groups()
    {
        var (svc, _) = Create();

        var page = await svc.ListExceptionSettingsAsync(null, null, false, 9, CancellationToken.None);

        Assert.Equal(9, page.Groups.Sum(g => g.Settings.Length));
        Assert.Contains(page.Groups, g => g.Truncated);
    }

    [Fact]
    public async Task List_unknown_group_lists_the_group_names_in_the_error()
    {
        var (svc, _) = Create();

        var e = await Assert.ThrowsAsync<BridgeException>(() => svc.ListExceptionSettingsAsync("Nope", null, true, 100, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, e.Code);
        Assert.Contains(Clr, e.Message, StringComparison.Ordinal);
        Assert.Contains("C++ Exceptions", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_changes_all_names_and_reports_previous_values()
    {
        var (svc, dte) = Create();

        var r = await svc.SetExceptionSettingsAsync(new object(), Clr, ["System.IO.IOException", "System.NullReferenceException"], true, CancellationToken.None);

        Assert.Equal(Clr, r.Group);
        Assert.Equal(
            [("System.IO.IOException", false, true, true), ("System.NullReferenceException", true, true, false)],
            r.Changes.Select(c => (c.Name, c.Previous, c.Current, c.Changed)));
        Assert.True(Value(dte, Clr, "System.IO.IOException"));
    }

    [Fact]
    public async Task Set_with_one_unknown_name_changes_nothing_and_offers_close_names()
    {
        var (svc, dte) = Create();

        var e = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetExceptionSettingsAsync(new object(), Clr, ["System.IO.IOException", "System.NullReferenceExeption"], true, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, e.Code);
        Assert.Contains("System.NullReferenceExeption", e.Message, StringComparison.Ordinal);
        Assert.Contains("System.NullReferenceException", e.Message, StringComparison.Ordinal);
        Assert.False(Value(dte, Clr, "System.IO.IOException"));
        Assert.DoesNotContain(dte.Calls, c => c.StartsWith("SetBreakWhenThrown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_without_its_namespace_is_offered_with_it()
    {
        var (svc, _) = Create();

        var e = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetExceptionSettingsAsync(new object(), Clr, ["TimeoutException"], true, CancellationToken.None));

        Assert.Contains("System.TimeoutException", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_most_five_close_names_are_offered()
    {
        var (svc, dte) = Create();
        var many = Enumerable.Range(0, 20).Select(i => ($"System.FooException{i:00}", false)).ToArray();
        dte.ExceptionGroupsData.Add(Group("Many", many));

        var e = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetExceptionSettingsAsync(new object(), "Many", ["System.FooException"], true, CancellationToken.None));

        Assert.Equal(5, many.Count(m => e.Message.Contains(m.Item1, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Names_are_matched_exactly_including_case()
    {
        var (svc, _) = Create();

        var e = await Assert.ThrowsAsync<BridgeException>(() =>
            svc.SetExceptionSettingsAsync(new object(), Clr, ["system.io.ioexception"], true, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task Set_to_the_current_value_is_unchanged_and_not_recorded()
    {
        var (svc, dte) = Create();
        var owner = new object();

        var r = await svc.SetExceptionSettingsAsync(owner, Clr, ["System.NullReferenceException"], true, CancellationToken.None);

        Assert.False(r.Changes[0].Changed);
        Assert.DoesNotContain(dte.Calls, c => c.StartsWith("SetBreakWhenThrown", StringComparison.Ordinal));
        Assert.Equal(0, await svc.PendingExceptionChangesAsync(owner));
    }

    [Fact]
    public async Task Duplicate_names_in_one_call_are_applied_once()
    {
        var (svc, dte) = Create();

        var r = await svc.SetExceptionSettingsAsync(new object(), Clr, ["System.IO.IOException", "System.IO.IOException"], true, CancellationToken.None);

        Assert.Single(r.Changes);
        Assert.Single(dte.Calls, c => c.StartsWith("SetBreakWhenThrown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_describes_the_change_without_writing()
    {
        var (svc, dte) = Create();

        var plan = await svc.PlanExceptionSettingsAsync(Clr, ["System.IO.IOException"], true, CancellationToken.None);

        Assert.True(plan.Changes[0].Changed);
        Assert.False(Value(dte, Clr, "System.IO.IOException"));
    }

    [Fact]
    public async Task Setting_twice_keeps_the_first_original()
    {
        var (svc, dte) = Create();
        var owner = new object();

        await svc.SetExceptionSettingsAsync(owner, Clr, ["System.IO.IOException"], true, CancellationToken.None);
        await svc.SetExceptionSettingsAsync(owner, Clr, ["System.IO.IOException"], false, CancellationToken.None);
        await svc.SetExceptionSettingsAsync(owner, Clr, ["System.IO.IOException"], true, CancellationToken.None);
        var reset = await svc.ResetExceptionSettingsAsync(owner, CancellationToken.None);

        Assert.False(Value(dte, Clr, "System.IO.IOException"));
        Assert.Single(reset.Restored);
    }

    [Fact]
    public async Task Reset_restores_only_what_this_owner_changed_and_clears_the_record()
    {
        var (svc, dte) = Create();
        var owner = new object();
        await svc.SetExceptionSettingsAsync(owner, Clr, ["System.IO.IOException", "System.NullReferenceException"], false, CancellationToken.None);
        dte.SetBreakWhenThrown(Clr, "System.TimeoutException", true);   // the user's own change

        var reset = await svc.ResetExceptionSettingsAsync(owner, CancellationToken.None);

        Assert.Equal([$"{Clr}/System.NullReferenceException"], reset.Restored.Select(c => c.Name));
        Assert.True(Value(dte, Clr, "System.NullReferenceException"));
        Assert.True(Value(dte, Clr, "System.TimeoutException"));
        Assert.Empty((await svc.ResetExceptionSettingsAsync(owner, CancellationToken.None)).Restored);
    }

    [Fact]
    public async Task Reset_does_not_touch_what_another_owner_changed()
    {
        var (svc, dte) = Create();
        await svc.SetExceptionSettingsAsync(new object(), Clr, ["System.IO.IOException"], true, CancellationToken.None);

        var reset = await svc.ResetExceptionSettingsAsync(new object(), CancellationToken.None);

        Assert.Empty(reset.Restored);
        Assert.True(Value(dte, Clr, "System.IO.IOException"));
    }

    [Fact]
    public async Task Reset_with_nothing_recorded_returns_empty()
    {
        var (svc, _) = Create();

        Assert.Empty((await svc.ResetExceptionSettingsAsync(new object(), CancellationToken.None)).Restored);
    }

    [Fact]
    public async Task Release_puts_back_what_the_owner_changed_and_forgets_it()
    {
        var (svc, dte) = Create();
        var owner = new object();
        await svc.SetExceptionSettingsAsync(owner, Clr, ["System.IO.IOException", "System.TimeoutException"], true, CancellationToken.None);

        await svc.ReleaseExceptionSettingsAsync(owner);

        Assert.False(Value(dte, Clr, "System.IO.IOException"));
        Assert.False(Value(dte, Clr, "System.TimeoutException"));
        Assert.Equal(0, await svc.PendingExceptionChangesAsync(owner));
        Assert.Empty((await svc.ResetExceptionSettingsAsync(owner, CancellationToken.None)).Restored);
    }

    [Fact]
    public async Task Release_of_an_owner_that_changed_nothing_writes_nothing()
    {
        var (svc, dte) = Create();

        await svc.ReleaseExceptionSettingsAsync(new object());

        Assert.DoesNotContain(dte.Calls, c => c.StartsWith("SetBreakWhenThrown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_halfway_puts_back_what_was_written_and_records_nothing()
    {
        var (svc, dte) = Create();
        var owner = new object();
        dte.FailSetBreakWhenThrownFor = "System.TimeoutException";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetExceptionSettingsAsync(owner, Clr, ["System.IO.IOException", "System.TimeoutException"], true, CancellationToken.None));

        Assert.False(Value(dte, Clr, "System.IO.IOException"));
        Assert.Equal(0, await svc.PendingExceptionChangesAsync(owner));
    }
}
