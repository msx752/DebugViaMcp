using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

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

    /// <summary>
    /// User review: with several Visual Studio instances open, attaching to another one would expose its memory (its
    /// pipe token, sources, credentials). Visual Studio and the debugviamcp server are never attach targets.
    /// </summary>
    [Theory]
    [InlineData("devenv")]
    [InlineData("DEVENV.exe")]
    [InlineData("debugviamcp")]
    [InlineData("DebugViaMcp.Mcp")]
    public void Other_visual_studio_instances_and_the_mcp_server_are_never_candidates(string name)
    {
        var all = new[] { new RawProcess { Pid = 500, Name = name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), OwnedByCurrentUser = true } };

        Assert.Empty(AttachCandidates.Filter(all, null));
        Assert.Empty(AttachCandidates.Filter(all, name));
    }

    [Fact]
    public async Task Attach_to_another_visual_studio_by_pid_is_not_found_before_asking()
    {
        var dte = new FakeDte { Mode = DebuggerMode.Design };
        dte.Processes.Add(new RawProcess { Pid = 600, Name = "devenv", OwnedByCurrentUser = true });
        using var s = new SecurityHarness(dte);
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;   // even an approving user must never get this far

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.AttachAsync(600, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.NotFound, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
        Assert.DoesNotContain("Attach:600", dte.Calls);
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

    /// <summary>Final review #5: the pid of the approved process was reused by another of the user's processes.</summary>
    [Fact]
    public async Task Attach_refuses_when_the_pid_now_names_another_process()
    {
        var dte = new FakeDte { Mode = DebuggerMode.Design };
        var svc = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        var approved = await svc.DescribeAttachTargetAsync(400, CancellationToken.None);
        dte.Processes.Single(p => p.Pid == 400).Name = "Notepad";      // the approved process exited; the pid was reused

        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.AttachAsync(400, approved, CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
        Assert.DoesNotContain("Attach:400", dte.Calls);
    }

    /// <summary>Review (architect): Visual Studio can attach and then throw; stop must still detach, never kill the user's process.</summary>
    [Fact]
    public async Task Stop_detaches_even_when_the_attach_call_failed_after_attaching()
    {
        FakeDte? self = null;
        var dte = new FakeDte { Mode = DebuggerMode.Design, OnAttach = _ => { self!.Mode = DebuggerMode.Run; throw new InvalidOperationException("late failure"); } };
        self = dte;                                        // Visual Studio attached (mode Run), then threw
        var svc = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AttachAsync(400, CancellationToken.None));

        await svc.StopAsync(CancellationToken.None);

        Assert.Contains("DetachAll", dte.Calls);
        Assert.DoesNotContain("Stop", dte.Calls);
    }

    [Fact]
    public async Task A_failed_attach_that_never_attached_leaves_stop_a_normal_stop()
    {
        var dte = new FakeDte { Mode = DebuggerMode.Design, OnAttach = _ => { throw new InvalidOperationException("refused"); } };
        var svc = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AttachAsync(400, CancellationToken.None));
        dte.Mode = DebuggerMode.Run;                       // a session started later by the user

        await svc.StopAsync(CancellationToken.None);

        Assert.Contains("Stop", dte.Calls);
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
