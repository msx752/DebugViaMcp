using StreamJsonRpc;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class LeaseTests
{
    private static readonly BreakpointSpec Spec = new() { File = "OrderService.cs", Line = 9 };

    private static Task<SetBreakpointResult> SetBp(BridgeConnectionHandler h) =>
        h.SetBreakpointAsync(Spec, SecurityHarness.Ctx(), CancellationToken.None);

    private static void AssertInUse(LocalRpcException ex) => Assert.Equal((int)BridgeErrorCode.InUse, ex.ErrorCode);

    /// <summary>Holds every UI-thread call while <see cref="Hold"/> is set.</summary>
    private sealed class SwitchUiThread : IUiThread
    {
        private volatile TaskCompletionSource? _hold;

        public void HoldCalls() => _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _hold?.TrySetResult();

        public async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
        {
            if (_hold is { } hold) await hold.Task.ConfigureAwait(false);
            return work();
        }
    }

    [Fact]
    public async Task First_state_changing_command_takes_the_lease_and_the_second_agent_gets_in_use()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");

        await SetBp(s.Handler);
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(b));

        AssertInUse(ex);
        Assert.Single(s.Dte.Bps);
    }

    [Fact]
    public async Task A_non_holder_cannot_read_either()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        AssertInUse(await Assert.ThrowsAsync<LocalRpcException>(() => b.ListBreakpointsAsync(SecurityHarness.Ctx(), CancellationToken.None)));
        AssertInUse(await Assert.ThrowsAsync<LocalRpcException>(() => b.GetStatusAsync(SecurityHarness.Ctx(), CancellationToken.None)));
        Assert.Single(await s.Handler.ListBreakpointsAsync(SecurityHarness.Ctx(), CancellationToken.None));
    }

    [Fact]
    public async Task Reading_never_takes_the_lease()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");

        await s.Handler.ListBreakpointsAsync(SecurityHarness.Ctx(), CancellationToken.None);
        await s.Handler.GetStatusAsync(SecurityHarness.Ctx(), CancellationToken.None);

        await SetBp(b);
        Assert.Single(s.Dte.Bps);
    }

    [Fact]
    public async Task The_error_names_the_holder_and_the_folder_and_escapes_them()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;   // this folder is not the solution's
        await s.HandshakeAsync("bo\nt", @"D:\x" + (char)0x202E + "y");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(b));

        Assert.Contains("bo" + "\\" + "nt", ex.Message, StringComparison.Ordinal);
        Assert.Contains(@"D:\\x" + "\\" + "u202ey", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', ex.Message);
        Assert.DoesNotContain((char)0x202E, ex.Message);
    }

    [Fact]
    public async Task The_error_caps_a_very_long_folder()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;   // this folder is not the solution's
        await s.HandshakeAsync("claude-code", @"D:\" + new string('x', 500));
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(b));

        Assert.True(ex.Message.Length < 300, ex.Message);
        Assert.Contains("…", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_lease_survives_debug_stop()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        s.Dte.Mode = DebuggerMode.Design;
        await s.Handler.StartAsync(SecurityHarness.Ctx(), CancellationToken.None);
        s.Dte.Mode = DebuggerMode.Break;
        await s.Handler.StopAsync(SecurityHarness.Ctx(), CancellationToken.None);

        AssertInUse(await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(b)));
    }

    [Fact]
    public async Task Release_frees_the_lease_and_is_audited()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        await s.Handler.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);
        await SetBp(b);

        var lines = s.AuditLines();
        Assert.Contains(lines, l => l.Contains("\"operation\":\"lease\"", StringComparison.Ordinal) && l.Contains("\"outcome\":\"acquired\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"operation\":\"lease\"", StringComparison.Ordinal) && l.Contains("\"outcome\":\"released\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Release_by_a_connection_that_holds_nothing_changes_nothing()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        await b.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);

        AssertInUse(await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(b)));
    }

    [Fact]
    public async Task Disposing_the_holders_connection_frees_the_lease()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        s.Handler.Dispose();

        await SetBp(b);
    }

    [Fact]
    public async Task Disconnect_AI_frees_the_lease_at_once_and_the_old_holder_never_gets_it_back()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        await SetBp(s.Handler);

        s.Security.DisconnectAi();
        s.Security.ReconnectAi();
        var again = await s.AddConnectionAsync("other-agent");

        await SetBp(again);
        var old = await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(s.Handler));
        Assert.Equal((int)BridgeErrorCode.AiDisabled, old.ErrorCode);
    }

    [Fact]
    public async Task A_call_denied_by_policy_does_not_take_the_lease()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        s.Settings.Permissions.Control = PermissionLevel.Deny;
        var denied = await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(s.Handler));
        Assert.Equal((int)BridgeErrorCode.Denied, denied.ErrorCode);
        s.Settings.Permissions.Control = PermissionLevel.Allow;

        await SetBp(b);
    }

    [Fact]
    public async Task A_call_the_user_denies_does_not_take_the_lease()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        s.Prompt.AutoAnswer = ConsentChoice.Deny;
        var denied = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.Denied, denied.ErrorCode);

        await SetBp(b);
    }

    [Fact]
    public async Task A_question_in_front_of_the_user_is_not_asked_when_the_lease_is_taken()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => b.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));

        AssertInUse(ex);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Concurrent_first_commands_have_exactly_one_winner()
    {
        var ui = new SwitchUiThread();
        using var s = new SecurityHarness(ui: ui);
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        ui.HoldCalls();

        var first = SetBp(s.Handler);
        var second = SetBp(b);
        await Task.WhenAny(first, second);   // the loser is refused at once, without touching Visual Studio
        ui.Release();
        var outcomes = await Task.WhenAll(Capture(first), Capture(second));

        Assert.Single(outcomes, e => e is null);
        AssertInUse(Assert.IsType<LocalRpcException>(Assert.Single(outcomes, e => e is not null)));
        Assert.Single(s.Dte.Bps);

        static async Task<Exception?> Capture(Task t)
        {
            try
            {
                await t;
                return null;
            }
            catch (LocalRpcException e)
            {
                return e;
            }
        }
    }

    /// <summary>Security review: a read that waited for the user's answer must not run if another agent took the lease meanwhile.</summary>
    [Fact]
    public async Task A_read_that_waited_for_the_user_is_refused_when_the_lease_was_taken_meanwhile()
    {
        using var s = new SecurityHarness();
        s.Settings.Permissions.Read = PermissionLevel.Ask;
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");

        var waiting = b.GetStatusAsync(SecurityHarness.Ctx("c-000050"), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        await SetBp(s.Handler);   // the first agent takes the lease while the question is open
        s.Prompt.Answer(ConsentChoice.AllowOnce);

        AssertInUse(await Assert.ThrowsAsync<LocalRpcException>(() => waiting));
    }

    /// <summary>Final review 1: Disconnect AI must free the lease even if its holder is no longer registered anywhere.</summary>
    [Fact]
    public async Task Disconnect_AI_clears_a_lease_whose_holder_is_not_registered()
    {
        using var s = new SecurityHarness();
        s.Security.Lease.TryAcquire(99, "ghost", null);

        s.Security.DisconnectAi();

        Assert.Null(s.Security.Lease.Holder);
        s.Security.ReconnectAi();
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(b);
    }

    /// <summary>Final review 6: the folder another agent reported is escaped and capped before it is stored, shown or published.</summary>
    [Fact]
    public async Task The_holders_folder_is_escaped_and_capped_before_it_is_published()
    {
        using var s = new SecurityHarness();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.HandshakeAsync("claude-code", @"D:\x" + (char)0x202E + new string('y', 500));
        await SetBp(s.Handler);

        var folder = s.Security.Lease.Holder!.Folder!;

        Assert.DoesNotContain((char)0x202E, folder);
        Assert.True(folder.Length <= ConsentText.MaxFolderLength + 1, folder);
        Assert.Contains("\\u202e", folder, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_works_even_when_reads_are_denied_by_settings()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        await SetBp(s.Handler);
        s.Settings.Permissions.Read = PermissionLevel.Deny;

        await s.Handler.ReleaseAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Null(s.Security.Lease.Holder);
    }

    [Fact]
    public async Task A_queued_question_is_not_shown_once_another_agent_holds_the_lease()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");

        var first = s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx("c-000001"), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        var second = b.SetValueAsync("total", "1", SecurityHarness.Ctx("c-000002"), CancellationToken.None);
        await Task.Delay(100);   // the second question is queued behind the first
        s.Prompt.Answer(ConsentChoice.AllowOnce);
        await first;

        AssertInUse(await Assert.ThrowsAsync<LocalRpcException>(() => second));
        Assert.Single(s.Prompt.Texts);
    }

    [Fact]
    public async Task Status_names_the_lease_holder()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        Assert.Null((await s.Handler.GetStatusAsync(SecurityHarness.Ctx(), CancellationToken.None)).LeaseHolder);
        await SetBp(s.Handler);

        var status = await s.Handler.GetStatusAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("claude-code", status.LeaseHolder);
    }

    [Fact]
    public async Task Refusals_are_audited()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var b = await s.AddConnectionAsync("other-agent");
        await SetBp(s.Handler);

        await Assert.ThrowsAsync<LocalRpcException>(() => SetBp(b));

        Assert.Single(s.AuditLines(), l => l.Contains("\"client\":\"other-agent\"", StringComparison.Ordinal) && l.Contains("\"outcome\":\"in_use\"", StringComparison.Ordinal));
    }
}
