using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>The guarded path of exception_list, exception_set and exception_reset: tier, question, audit, lease, and what a connection leaves behind.</summary>
public class ExceptionSettingsBridgeTests
{
    private const string Clr = "Common Language Runtime Exceptions";
    private static readonly string[] NullRef = ["System.NullReferenceException"];

    private static SecurityHarness Create()
    {
        var s = new SecurityHarness();
        s.Dte.ExceptionGroupsData.Add(new RawExceptionGroup
        {
            Name = Clr,
            Settings =
            [
                new RawExceptionSetting { Name = "System.NullReferenceException", BreakWhenThrown = false },
                new RawExceptionSetting { Name = "System.IO.IOException", BreakWhenThrown = true },
                new RawExceptionSetting { Name = "System.TimeoutException", BreakWhenThrown = false },
            ],
        });
        return s;
    }

    private static bool Value(SecurityHarness s, string name) =>
        s.Dte.ExceptionGroupsData.Single().Settings.Single(x => x.Name == name).BreakWhenThrown;

    [Fact]
    public async Task Set_asks_with_the_group_and_the_names_that_change()
    {
        using var s = Create();
        s.Settings.Permissions.Control = PermissionLevel.Ask;
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.SetExceptionSettingsAsync(Clr, ["System.NullReferenceException", "System.IO.IOException"], true, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal($"claude-code wants to turn Break When Thrown on for 1 exception in {Clr}: System.NullReferenceException", Assert.Single(s.Prompt.Texts));
        Assert.True(Value(s, "System.NullReferenceException"));
    }

    [Fact]
    public async Task Set_with_an_unknown_name_never_asks()
    {
        using var s = Create();
        s.Settings.Permissions.Control = PermissionLevel.Ask;
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.SetExceptionSettingsAsync(Clr, ["System.Bogus"], true, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.NotFound, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Denied_by_policy_when_Control_is_Deny_and_nothing_is_touched()
    {
        using var s = Create();
        s.Settings.Permissions.Control = PermissionLevel.Deny;
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.DoesNotContain(s.Dte.Calls, c => c.StartsWith("SetBreakWhenThrown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Set_is_audited_with_the_call_id_and_the_control_tier()
    {
        using var s = Create();
        await s.HandshakeAsync();

        await s.Handler.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx("c-000042"), CancellationToken.None);

        Assert.Contains(s.AuditLines(), l => l.Contains("c-000042", StringComparison.Ordinal)
            && l.Contains("\"operation\":\"exception_set\"", StringComparison.Ordinal) && l.Contains("\"tier\":\"control\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Set_takes_the_lease_and_list_does_not()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        var other = await s.AddConnectionAsync("other-agent");

        await s.Handler.ListExceptionSettingsAsync(null, null, true, 100, SecurityHarness.Ctx(), CancellationToken.None);
        await other.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx(), CancellationToken.None);   // list took nothing: the other agent still gets in

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.SetExceptionSettingsAsync(Clr, ["System.TimeoutException"], true, SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.InUse, ex.ErrorCode);
    }

    [Fact]
    public async Task List_is_refused_like_every_read_while_another_agent_holds_the_lease()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        var other = await s.AddConnectionAsync("other-agent");
        await other.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.ListExceptionSettingsAsync(null, null, true, 100, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.InUse, ex.ErrorCode);
    }

    [Fact]
    public async Task Reset_asks_with_the_count_and_puts_back_what_the_connection_changed()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        await s.Handler.SetExceptionSettingsAsync(Clr, ["System.NullReferenceException", "System.TimeoutException"], true, SecurityHarness.Ctx(), CancellationToken.None);
        s.Settings.Permissions.Control = PermissionLevel.Ask;
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        var result = await s.Handler.ResetExceptionSettingsAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("claude-code wants to restore Break When Thrown for 2 exceptions it changed", Assert.Single(s.Prompt.Texts));
        Assert.Equal(2, result.Restored.Length);
        Assert.False(Value(s, "System.NullReferenceException"));
        Assert.False(Value(s, "System.TimeoutException"));
    }

    [Fact]
    public async Task A_closed_connection_leaves_no_record_for_the_next_one()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        var gone = await s.AddConnectionAsync("gone-agent");
        await gone.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx(), CancellationToken.None);
        gone.Dispose();

        var result = await s.Handler.ResetExceptionSettingsAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Empty(result.Restored);
        Assert.True(Value(s, "System.NullReferenceException"));   // the record is dropped, not applied: settings return to the user's when Visual Studio restarts
    }

    [Fact]
    public async Task Giving_up_the_lease_drops_the_record_so_a_stale_reset_cannot_write()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        await s.Handler.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx(), CancellationToken.None);

        await s.Handler.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);
        var other = await s.AddConnectionAsync("other-agent");
        await other.SetExceptionSettingsAsync(Clr, NullRef, false, SecurityHarness.Ctx(), CancellationToken.None);
        await other.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);
        var stale = await s.Handler.ResetExceptionSettingsAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Empty(stale.Restored);
        Assert.False(Value(s, "System.NullReferenceException"));   // the other agent's value stays; the first agent's old record did not undo it
    }

    [Fact]
    public async Task Reset_with_nothing_to_restore_neither_asks_nor_takes_the_lease()
    {
        using var s = Create();
        s.Settings.Permissions.Control = PermissionLevel.Ask;
        await s.HandshakeAsync("claude-code");
        var other = await s.AddConnectionAsync("other-agent");

        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        var result = await s.Handler.ResetExceptionSettingsAsync(SecurityHarness.Ctx(), CancellationToken.None);
        await other.SetExceptionSettingsAsync(Clr, NullRef, true, SecurityHarness.Ctx(), CancellationToken.None);   // not locked out by the cleanup call

        Assert.Empty(result.Restored);
        Assert.DoesNotContain(s.Prompt.Texts, t => t.Contains("restore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Calls_before_the_handshake_are_unauthorized()
    {
        using var s = Create();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.ListExceptionSettingsAsync(null, null, true, 100, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.Unauthorized, ex.ErrorCode);
    }
}
