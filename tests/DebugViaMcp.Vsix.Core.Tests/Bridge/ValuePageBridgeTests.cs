using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>The guarded path of a paged read: the Read tier, like eval without side effects.</summary>
public class ValuePageBridgeTests
{
    private static SecurityHarness Create()
    {
        var s = new SecurityHarness();
        s.Dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = "\"hello world\"", Type = "string" };
        return s;
    }

    [Fact]
    public async Task A_page_is_read_with_the_default_Read_tier_and_asks_nothing()
    {
        using var s = Create();
        await s.HandshakeAsync();

        var page = await s.Handler.ReadValuePageAsync("text", 1, 5, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("hello", page.Value);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task A_page_is_denied_by_policy_when_Read_is_Deny()
    {
        using var s = Create();
        s.Settings.Permissions.Read = PermissionLevel.Deny;
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.ReadValuePageAsync("text", 0, 5, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.Null(s.Dte.LastEvaluated);
    }

    [Fact]
    public async Task A_page_asks_like_eval_when_Read_is_Ask()
    {
        using var s = Create();
        s.Settings.Permissions.Read = PermissionLevel.Ask;
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.ReadValuePageAsync("text", 0, 5, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Contains("eval", Assert.Single(s.Prompt.Texts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bad_range_is_an_invalid_argument()
    {
        using var s = Create();
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.ReadValuePageAsync("text", -1, 5, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.InvalidArgument, ex.ErrorCode);
    }
}
