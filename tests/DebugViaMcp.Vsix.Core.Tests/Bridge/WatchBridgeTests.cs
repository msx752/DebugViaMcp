using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>The guarded path of watch_add, watch_list and watch_remove: tier, question, audit, lease, and what a connection leaves behind.</summary>
public class WatchBridgeTests
{
    private static SecurityHarness Create()
    {
        var s = new SecurityHarness();
        s.Dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = "3", Type = "int" };
        s.Dte.ExceptionGroupsData.Add(new RawExceptionGroup { Name = "G", Settings = [new RawExceptionSetting { Name = "E", BreakWhenThrown = false }] });
        return s;
    }

    [Fact]
    public async Task Add_works_with_the_default_Read_tier_and_asks_nothing()
    {
        using var s = Create();
        await s.HandshakeAsync();

        var added = await s.Handler.AddWatchAsync("i", null, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("3", added.Watch.Value);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Add_is_denied_by_policy_when_Read_is_Deny_and_nothing_is_stored()
    {
        using var s = Create();
        s.Settings.Permissions.Read = PermissionLevel.Deny;
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.AddWatchAsync("i", null, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        s.Settings.Permissions.Read = PermissionLevel.Allow;
        Assert.Empty((await s.Handler.ListWatchesAsync(SecurityHarness.Ctx(), CancellationToken.None)).Watches);
    }

    [Fact]
    public async Task Add_asks_once_when_Read_is_Ask_and_names_the_expression()
    {
        using var s = Create();
        s.Settings.Permissions.Read = PermissionLevel.Ask;
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.AddWatchAsync("order.Total", null, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Contains("order.Total", Assert.Single(s.Prompt.Texts), StringComparison.Ordinal);
    }

    /// <summary>Like every Read call, an answered question is audited and a plain allowed call is not.</summary>
    [Fact]
    public async Task Add_is_audited_with_the_call_id_when_it_asked()
    {
        using var s = Create();
        s.Settings.Permissions.Read = PermissionLevel.Ask;
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.AddWatchAsync("i", null, SecurityHarness.Ctx("c-000077"), CancellationToken.None);

        Assert.Contains(s.AuditLines(), l => l.Contains("c-000077", StringComparison.Ordinal)
            && l.Contains("\"operation\":\"watch_add\"", StringComparison.Ordinal) && l.Contains("\"tier\":\"read\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_does_not_take_the_lease()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        var other = await s.AddConnectionAsync("other-agent");

        await s.Handler.AddWatchAsync("i", null, SecurityHarness.Ctx(), CancellationToken.None);

        // A watch changes nothing in Visual Studio: the other agent can still take the lease with a change of its own.
        await other.SetExceptionSettingsAsync("G", ["E"], true, SecurityHarness.Ctx(), CancellationToken.None);
    }

    [Fact]
    public async Task A_refused_expression_is_an_invalid_argument()
    {
        using var s = Create();
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.AddWatchAsync("a; b", null, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.InvalidArgument, ex.ErrorCode);
    }

    [Fact]
    public async Task Releasing_the_hold_drops_the_list()
    {
        using var s = Create();
        await s.HandshakeAsync();
        await s.Handler.AddWatchAsync("i", null, SecurityHarness.Ctx(), CancellationToken.None);

        await s.Handler.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Empty((await s.Handler.ListWatchesAsync(SecurityHarness.Ctx(), CancellationToken.None)).Watches);
    }

    [Fact]
    public async Task A_connection_that_goes_away_leaves_no_list_for_the_next_one()
    {
        using var s = Create();
        await s.HandshakeAsync("claude-code");
        var gone = await s.AddConnectionAsync("other-agent");
        await gone.AddWatchAsync("i", null, SecurityHarness.Ctx(), CancellationToken.None);

        gone.Dispose();
        var next = await s.AddConnectionAsync("third-agent");

        Assert.Empty((await next.ListWatchesAsync(SecurityHarness.Ctx(), CancellationToken.None)).Watches);
    }
}
