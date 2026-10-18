using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Security;

public class ConnectionApprovalTests
{
    private const string Elsewhere = @"D:\elsewhere";

    private static Task<StatusInfo> Status(BridgeConnectionHandler h, string id = "c-000001") =>
        h.GetStatusAsync(SecurityHarness.Ctx(id), CancellationToken.None);

    private static Task<SetBreakpointResult> SetBp(BridgeConnectionHandler h) =>
        h.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, SecurityHarness.Ctx(), CancellationToken.None);

    [Fact]
    public async Task A_client_in_the_solution_folder_is_approved_without_a_question()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code", @"C:\src\Demo");

        await Status(s.Handler);
        await SetBp(s.Handler);

        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task A_client_in_a_subfolder_is_approved_without_a_question()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code", @"C:\src\Demo\tools\scripts");

        await Status(s.Handler);

        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task A_client_in_an_unrelated_folder_is_asked_once()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", Elsewhere);

        await Status(s.Handler);
        await Status(s.Handler, "c-000002");
        await SetBp(s.Handler);

        var text = Assert.Single(s.Prompt.Texts);
        Assert.StartsWith("claude-code", text, StringComparison.Ordinal);
        Assert.Contains("wants to control this Visual Studio", text, StringComparison.Ordinal);
        Assert.Contains("elsewhere", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allow_for_the_session_approves_the_connection_too()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowSession;
        await s.HandshakeAsync("claude-code", Elsewhere);

        await Status(s.Handler);
        await Status(s.Handler, "c-000002");

        Assert.Single(s.Prompt.Texts);
    }

    [Fact]
    public async Task A_client_above_the_solution_folder_is_asked()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", @"C:\src");   // selection would accept it; trust is stricter

        await Status(s.Handler);

        Assert.Single(s.Prompt.Texts);
    }

    [Fact]
    public async Task A_client_with_no_folder_is_asked()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", null);

        await Status(s.Handler);

        var text = Assert.Single(s.Prompt.Texts);
        Assert.Contains("did not report a folder", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_solution_loaded_means_ask()
    {
        using var s = new SecurityHarness();
        s.Dte.SolutionPath = null;
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", @"C:\src\Demo");

        await Status(s.Handler);

        Assert.Single(s.Prompt.Texts);
    }

    [Fact]
    public async Task Deny_answers_denied_user_denied_and_runs_nothing()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.Deny;
        await s.HandshakeAsync("claude-code", Elsewhere);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(s.Handler));

        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.Equal(DeniedReasons.UserDenied, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Empty(s.Dte.Bps);
        Assert.Empty(s.Dte.Calls);
        Assert.Contains(s.AuditLines(), l => l.Contains("\"operation\":\"connect\"", StringComparison.Ordinal) && l.Contains("\"decision\":\"denied:user\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_approval_audit_entry_carries_the_call_id()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", Elsewhere);

        await Status(s.Handler, "c-000777");

        Assert.Contains(s.AuditLines(), l => l.Contains("\"operation\":\"connect\"", StringComparison.Ordinal) && l.Contains("c-000777", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_denied_connection_is_asked_again_on_the_next_call()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.Deny;
        await s.HandshakeAsync("claude-code", Elsewhere);
        await Assert.ThrowsAsync<LocalRpcException>(() => Status(s.Handler));

        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await Status(s.Handler, "c-000002");

        Assert.Equal(2, s.Prompt.Texts.Count);
    }

    [Fact]
    public async Task Timeout_answers_denied_timeout()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code", Elsewhere);

        var call = Status(s.Handler);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Delay.Elapse(0);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.Equal(DeniedReasons.Timeout, ((BridgeErrorData)ex.ErrorData!).Reason);
    }

    [Fact]
    public async Task Reads_need_approval_too()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", Elsewhere);

        await s.Handler.ListBreakpointsAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Single(s.Prompt.Texts);
    }

    [Fact]
    public async Task Release_needs_no_approval()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code", Elsewhere);

        await s.Handler.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Concurrent_first_calls_ask_a_single_question()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code", Elsewhere);

        var first = Status(s.Handler, "c-000001");
        var second = Status(s.Handler, "c-000002");
        await s.Prompt.WaitForPromptsAsync(1);
        await Task.Delay(100);   // the second call must be waiting for the first answer, not asking its own question
        s.Prompt.Answer(ConsentChoice.AllowOnce);
        await Task.WhenAll(first, second);

        Assert.Single(s.Prompt.Texts);
    }

    [Fact]
    public async Task A_call_denied_by_policy_asks_no_connection_question()
    {
        using var s = new SecurityHarness();
        s.Settings.Permissions.Read = PermissionLevel.Deny;
        await s.HandshakeAsync("claude-code", Elsewhere);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => Status(s.Handler));

        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task The_question_escapes_client_and_folder()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("bo\nt", @"D:\x" + (char)0x202E + "y");

        await Status(s.Handler);

        var text = Assert.Single(s.Prompt.Texts);
        Assert.Contains("bo" + "\\" + "nt", text, StringComparison.Ordinal);
        Assert.Contains(@"D:\\x" + "\\" + "u202ey", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain((char)0x202E, text);
    }

    [Fact]
    public async Task The_question_caps_a_very_long_folder()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", @"D:\" + new string('x', 500));

        await Status(s.Handler);

        var text = Assert.Single(s.Prompt.Texts);
        Assert.True(text.Length < 300, text);
        Assert.Contains("…", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_belongs_to_the_connection()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", Elsewhere);
        var other = await s.AddConnectionAsync("other-agent", Elsewhere);

        await Status(s.Handler);
        await Status(other);

        Assert.Equal(2, s.Prompt.Texts.Count);
    }

    [Fact]
    public async Task Approval_is_asked_before_the_lease_is_checked()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");   // trusted folder: takes the lease without a question
        var unapproved = await s.AddConnectionAsync("other-agent", Elsewhere);
        await SetBp(s.Handler);

        var call = unapproved.ListBreakpointsAsync(SecurityHarness.Ctx(), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Prompt.Answer(ConsentChoice.AllowOnce);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal((int)BridgeErrorCode.InUse, ex.ErrorCode);
        Assert.Single(s.Prompt.Texts);
    }
}
