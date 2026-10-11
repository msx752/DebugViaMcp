using StreamJsonRpc;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class AttachTests
{
    [Fact]
    public void Candidates_are_own_processes_matched_by_name_with_or_without_exe()
    {
        var all = new FakeDte().Processes;

        Assert.Equal([100, 200], AttachCandidates.Filter(all, "faultdemo.exe").Select(p => p.Pid));
        Assert.Equal([400], AttachCandidates.Filter(all, "OrderDemo").Select(p => p.Pid));
        Assert.Empty(AttachCandidates.Filter(all, "lsass"));                    // another user's process
        Assert.Equal([100, 200, 400], AttachCandidates.Filter(all, null).Select(p => p.Pid));
    }

    [Fact]
    public async Task Attach_asks_with_process_name_pid_and_memory_warning()
    {
        using var s = new SecurityHarness(new FakeDte { Mode = DebuggerMode.Design });
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.AttachAsync(400, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("claude-code wants to attach the debugger to OrderDemo (pid 400) — Visual Studio will be able to read and change that process's memory", Assert.Single(s.Prompt.Texts));
        Assert.Contains("Attach:400", s.Dte.Calls);
    }

    [Fact]
    public async Task Denied_attach_never_attaches()
    {
        using var s = new SecurityHarness(new FakeDte { Mode = DebuggerMode.Design });
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.Deny;

        await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.AttachAsync(400, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.DoesNotContain("Attach:400", s.Dte.Calls);
    }

    [Theory]
    [InlineData(300)]   // owned by another user
    [InlineData(999)]   // does not exist
    public async Task Attach_to_a_process_that_is_not_a_candidate_fails_before_asking(int pid)
    {
        using var s = new SecurityHarness(new FakeDte { Mode = DebuggerMode.Design });
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.AttachAsync(pid, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.NotFound, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Attach_while_debugging_is_wrong_mode_before_asking()
    {
        using var s = new SecurityHarness(new FakeDte { Mode = DebuggerMode.Break });
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.AttachAsync(400, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.WrongMode, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Stop_after_attach_detaches_instead_of_terminating()
    {
        var dte = new FakeDte { Mode = DebuggerMode.Design };
        var svc = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        await svc.AttachAsync(400, CancellationToken.None);
        dte.Mode = DebuggerMode.Run;

        await svc.StopAsync(CancellationToken.None);

        Assert.Contains("DetachAll", dte.Calls);
        Assert.DoesNotContain("Stop", dte.Calls);

        svc.OnEnterDesign();
        dte.Mode = DebuggerMode.Design;
        await svc.StartAsync(CancellationToken.None);   // a later launched session…
        dte.Mode = DebuggerMode.Run;
        await svc.StopAsync(CancellationToken.None);    // …is stopped normally
        Assert.Contains("Stop", dte.Calls);
    }
}
