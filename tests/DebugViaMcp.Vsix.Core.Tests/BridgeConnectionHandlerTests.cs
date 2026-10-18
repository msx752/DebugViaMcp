using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

public class BridgeConnectionHandlerTests
{
    private static readonly CallContext Ctx = new() { CallId = "c-test" };

    private static BridgeConnectionHandler Create(FakeDte? dte = null, Action<int>? onAuthenticated = null)
    {
        var settings = new TestSettings();
        var security = new BridgeSecurity(settings, new ConsentGate(new FakePrompt(), () => settings.ConsentTimeout),
            new AuditLog(Path.Combine(Path.GetTempPath(), "debugviamcp-tests-audit"), () => 7, new NullActivity(), new NullLog()), new AiAccessSwitch(enabled: true));
        return new(new DebugBridgeService(dte ?? new FakeDte(), new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), "secret", security, new NullLog(),
            onAuthenticated is null ? null : new ConnectionHooks(onAuthenticated, () => Task.CompletedTask));
    }

    private sealed class NullActivity : IActivitySink
    {
        public void Write(string line)
        {
        }
    }

    /// <summary>Found by the M1 end-to-end run: the handshake read DTE state off the UI thread.</summary>
    [Fact]
    public async Task Handshake_reads_vs_state_on_the_ui_thread()
    {
        var h = Create(new FakeDte { EnforceUiThread = true });

        var r = await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);

        Assert.Equal("18.10", r.VsVersion);
        Assert.Equal(@"C:\src\Demo\Demo.sln", r.SolutionPath);
    }

    [Fact]
    public async Task Every_operation_touches_vs_only_on_the_ui_thread()
    {
        var h = Create(new FakeDte { EnforceUiThread = true });
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);

        await h.GetStatusAsync(Ctx, CancellationToken.None);
        await h.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, Ctx, CancellationToken.None);
        await h.ListBreakpointsAsync(Ctx, CancellationToken.None);
        await h.GetStackAsync(5, Ctx, CancellationToken.None);
        await h.GetVariablesAsync(VariableScope.Args, null, 0, 10, Ctx, CancellationToken.None);
        await h.EvaluateAsync("x", allowSideEffects: false, Ctx, CancellationToken.None);
        await h.StepAsync(StepKind.Over, Ctx, CancellationToken.None);
        await h.RemoveBreakpointsAsync(null, all: true, Ctx, CancellationToken.None);
        await h.GetThreadsAsync(Ctx, CancellationToken.None);
        await h.GetThreadStackAsync(null, 5, Ctx, CancellationToken.None);
        await h.SelectFrameAsync(0, 0, null, Ctx, CancellationToken.None);
    }

    [Fact]
    public async Task Calls_before_handshake_are_unauthorized()
    {
        var h = Create();
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => h.GetStatusAsync(Ctx, CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.Unauthorized, ex.ErrorCode);
    }

    [Fact]
    public async Task Wrong_token_is_rejected()
    {
        var h = Create();
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.HandshakeAsync(new HandshakeRequest { Token = "nope", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.Unauthorized, ex.ErrorCode);
    }

    [Fact]
    public async Task Major_version_mismatch_is_rejected_with_code()
    {
        var h = Create();
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major + 1 }, CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.VersionMismatch, ex.ErrorCode);
    }

    [Fact]
    public async Task OnAuthenticated_fires_only_after_valid_handshake()
    {
        var fired = 0;
        var h = Create(onAuthenticated: _ => fired++);

        await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.HandshakeAsync(new HandshakeRequest { Token = "nope", ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None));
        Assert.Equal(0, fired);

        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Valid_handshake_returns_versions_and_unlocks_calls()
    {
        var h = Create();
        var r = await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major, ClientName = "t" }, CancellationToken.None);

        Assert.Equal(ProtocolInfo.Major, r.ProtocolMajor);
        Assert.Equal("0.1.0", r.VsixVersion);
        Assert.Equal(DebuggerMode.Break, (await h.GetStatusAsync(Ctx, CancellationToken.None)).Mode);
    }

    [Fact]
    public async Task Bridge_exception_becomes_coded_rpc_error_with_hint()
    {
        var h = Create(new FakeDte { FileExistsFunc = _ => false });
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            h.SetBreakpointAsync(new BreakpointSpec { File = "missing.cs", Line = 1 }, Ctx, CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.InvalidArgument, ex.ErrorCode);
        Assert.Equal("Use a path relative to the solution directory.", Assert.IsType<BridgeErrorData>(ex.ErrorData).Hint);
    }

    [Fact]
    public async Task Handshake_reports_permissions_and_log_level()
    {
        using var s = new SecurityHarness();
        s.Settings.Permissions.Mutate = PermissionLevel.Deny;
        s.Settings.LogLevel = LogVerbosity.Debug;

        var r = await s.HandshakeAsync();

        Assert.Equal(PermissionLevel.Deny, r.Permissions.Mutate);
        Assert.Equal(LogVerbosity.Debug, r.LogLevel);
        Assert.Equal(7, r.LogRetentionDays);
    }

    [Fact]
    public async Task Handshake_reports_the_log_retention_from_the_options()
    {
        using var s = new SecurityHarness();
        s.Settings.LogRetentionDays = 3;

        var r = await s.HandshakeAsync();

        Assert.Equal(3, r.LogRetentionDays);
    }

    [Fact]
    public async Task Status_reports_current_permissions()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var status = await s.Handler.GetStatusAsync(SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal(PermissionLevel.Ask, status.Permissions!.Mutate);
    }

    /// <summary>Fitness function (spec §8): with Mutate = Ask and no answer, set_value never reaches Visual Studio.</summary>
    [Fact]
    public async Task Set_value_without_an_answer_never_reaches_visual_studio()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var call = s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Delay.Elapse(0);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.Equal(DeniedReasons.Timeout, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Contains("60 s", ex.Message, StringComparison.Ordinal);
        Assert.Empty(s.Dte.Assignments);
    }

    [Fact]
    public async Task Set_value_prompt_shows_client_target_value_and_function()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        var r = await s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("claude-code wants to set total = 0 in Demo.OrderService.Submit(Demo.Order order)", Assert.Single(s.Prompt.Texts));
        Assert.Equal("0", r.Value);
        Assert.Single(s.Dte.Assignments);
    }

    [Fact]
    public async Task User_deny_is_denied_and_never_reaches_visual_studio()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.Deny;

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal(DeniedReasons.UserDenied, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Contains("do not retry", ((BridgeErrorData)ex.ErrorData!).Hint, StringComparison.Ordinal);
        Assert.Empty(s.Dte.Assignments);
    }

    [Fact]
    public async Task Policy_deny_does_not_prompt()
    {
        using var s = new SecurityHarness();
        s.Settings.Permissions.Mutate = PermissionLevel.Deny;
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal(DeniedReasons.Policy, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Contains("Mutate = Deny", ex.Message, StringComparison.Ordinal);
        Assert.Empty(s.Prompt.Texts);
        Assert.Empty(s.Dte.Assignments);
    }

    [Fact]
    public async Task Control_set_to_ask_prompts_for_steps()
    {
        using var s = new SecurityHarness();
        s.Settings.Permissions.Control = PermissionLevel.Ask;
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.StepAsync(StepKind.Over, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("claude-code wants to run debug_step: over", Assert.Single(s.Prompt.Texts));
        Assert.Contains("Step:Over", s.Dte.Calls);
    }

    /// <summary>Review focus #5.</summary>
    [Fact]
    public async Task Wrong_mode_fails_before_prompting()
    {
        using var s = new SecurityHarness(new FakeDte { Mode = DebuggerMode.Run });
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.WrongMode, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
    }

    /// <summary>Review focus #2.</summary>
    [Fact]
    public async Task Overlong_value_is_rejected_before_prompting()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", new string('1', 10_000), SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.InvalidArgument, ex.ErrorCode);
        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Side_effect_evaluation_is_gated_but_plain_evaluation_is_not()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.EvaluateAsync("order.Touch()", allowSideEffects: false, SecurityHarness.Ctx(), CancellationToken.None);
        Assert.Empty(s.Prompt.Texts);

        await s.Handler.EvaluateAsync("order.Touch()", allowSideEffects: true, SecurityHarness.Ctx(), CancellationToken.None);
        Assert.Equal("claude-code wants to evaluate order.Touch() with side effects in Demo.OrderService.Submit(Demo.Order order)", Assert.Single(s.Prompt.Texts));
        Assert.Equal("order.Touch()", s.Dte.LastEvaluated);
    }

    /// <summary>Review focus #4.</summary>
    [Fact]
    public async Task Settings_change_clears_session_grants()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowSession;
        await s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);

        s.Settings.Permissions.Mutate = PermissionLevel.Deny;
        s.Security.SettingsChanged("Mutate: Ask → Deny");

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", "1", SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Equal(DeniedReasons.Policy, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Single(s.Dte.Assignments);
        Assert.Contains(s.AuditLines(), l => l.Contains("\"operation\":\"settings\"", StringComparison.Ordinal));
    }

    /// <summary>Fitness function: every control/mutate call is audited exactly once with its callId.</summary>
    [Fact]
    public async Task Control_and_mutate_calls_are_audited_once_and_reads_are_not()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.GetStackAsync(5, SecurityHarness.Ctx("c-000010"), CancellationToken.None);
        await s.Handler.StepAsync(StepKind.Over, SecurityHarness.Ctx("c-000011"), CancellationToken.None);
        await s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx("c-000012"), CancellationToken.None);

        var lines = s.AuditLines();
        Assert.DoesNotContain(lines, l => l.Contains("c-000010", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("c-000011", StringComparison.Ordinal) && l.Contains("\"decision\":\"allow\"", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("c-000012", StringComparison.Ordinal) && l.Contains("\"decision\":\"allowOnce\"", StringComparison.Ordinal) && l.Contains("\"client\":\"claude-code\"", StringComparison.Ordinal));
        Assert.Contains(s.Activity, l => l.Contains("c-000012  set_value  total = 0  → allowed (once)  ok", StringComparison.Ordinal));
    }

    /// <summary>Fitness function: a debuggee value never reaches the audit log.</summary>
    [Fact]
    public async Task Debuggee_values_never_reach_the_audit_log()
    {
        using var s = new SecurityHarness();
        s.Dte.OnSetValue = (_, _) => new EvaluateResult { IsValid = true, Value = "s3cr3t-token-from-debuggee", Type = "string" };
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.SetValueAsync("token", "\"x\"", SecurityHarness.Ctx(), CancellationToken.None);
        await s.Handler.EvaluateAsync("token", allowSideEffects: true, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.DoesNotContain(s.AuditLines(), l => l.Contains("s3cr3t", StringComparison.Ordinal));
        Assert.DoesNotContain(s.Activity, l => l.Contains("s3cr3t", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connect_and_disconnect_are_audited()
    {
        var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        s.Dispose();

        Assert.Contains(s.Activity, l => l.Contains("  connect  claude-code", StringComparison.Ordinal));
        Assert.Contains(s.Activity, l => l.Contains("  disconnect  claude-code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handshake_while_ai_is_disconnected_is_ai_disabled()
    {
        using var s = new SecurityHarness();
        s.Access.Disconnect();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.HandshakeAsync());

        Assert.Equal((int)BridgeErrorCode.AiDisabled, ex.ErrorCode);
        Assert.Contains("Extensions → Debugging via MCP → Reconnect AI", ((BridgeErrorData)ex.ErrorData!).Hint, StringComparison.Ordinal);
    }

    /// <summary>Review focus #3.</summary>
    [Fact]
    public async Task Disconnect_ai_while_prompting_denies_and_drops()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        Assert.Equal(["claude-code"], s.Access.Clients);

        var call = s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx("c-000020"), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Security.DisconnectAi();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal(DeniedReasons.Disconnected, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Equal(1, s.Drops);
        Assert.True(s.RevokedNotified);
        Assert.Empty(s.Dte.Assignments);
        Assert.Contains(s.AuditLines(), l => l.Contains("c-000020", StringComparison.Ordinal) && l.Contains("denied:disconnected", StringComparison.Ordinal));
        Assert.Contains(s.Activity, l => l.Contains("ai_disconnect", StringComparison.Ordinal));
    }

    /// <summary>Security review: conditions and tracepoint placeholders run in the debuggee on every hit (no ', nse'), so they need consent.</summary>
    [Fact]
    public async Task Breakpoint_condition_is_mutate_tier_and_asks_by_default()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.Deny;

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "order.Total > 1000" }, SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal(DeniedReasons.UserDenied, ((BridgeErrorData)ex.ErrorData!).Reason);
        Assert.Contains("order.Total > 1000", Assert.Single(s.Prompt.Texts), StringComparison.Ordinal);
        Assert.Contains("runs in your program on every hit", s.Prompt.Texts[0], StringComparison.Ordinal);
        Assert.Empty(s.Dte.Bps);
    }

    [Fact]
    public async Task Tracepoint_message_is_mutate_tier()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, LogMessage = "total={total}" }, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Single(s.Prompt.Texts);
        Assert.Single(s.Dte.Bps);
    }

    [Fact]
    public async Task Plain_breakpoint_stays_control_tier()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        await s.Handler.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Empty(s.Prompt.Texts);
        Assert.Single(s.Dte.Bps);
    }

    /// <summary>Security review: a call the settings deny must not touch Visual Studio at all, not even to word a question.</summary>
    [Fact]
    public async Task Policy_deny_wins_before_any_preparation()
    {
        using var s = new SecurityHarness(new FakeDte { Mode = DebuggerMode.Run });
        s.Settings.Permissions.Mutate = PermissionLevel.Deny;
        await s.HandshakeAsync();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.Denied, ex.ErrorCode);
        Assert.Equal(DeniedReasons.Policy, ((BridgeErrorData)ex.ErrorData!).Reason);
    }

    /// <summary>Security review: once the user chose Disconnect AI, nothing on that connection may run, even before the pipe closes.</summary>
    [Fact]
    public async Task Calls_after_disconnect_ai_are_refused_even_before_the_pipe_closes()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        s.Security.DisconnectAi();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.StepAsync(StepKind.Over, SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.AiDisabled, ex.ErrorCode);
        Assert.DoesNotContain("Step:Over", s.Dte.Calls);
    }

    [Fact]
    public async Task Reconnect_ai_does_not_revive_a_revoked_connection()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        s.Security.DisconnectAi();
        s.Security.ReconnectAi();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.StepAsync(StepKind.Over, SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.AiDisabled, ex.ErrorCode);
        Assert.DoesNotContain("Step:Over", s.Dte.Calls);
    }

    /// <summary>Final review I1: the pipe drops while the question is being prepared — no question, no change.</summary>
    [Fact]
    public async Task Connection_closed_while_preparing_never_prompts_or_assigns()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        s.Dte.StackGate = new ManualResetEventSlim(false);

        var call = Task.Run(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));
        await s.Dte.StackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        s.Handler.Dispose();
        s.Dte.StackGate.Set();

        await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Empty(s.Prompt.Texts);
        Assert.Empty(s.Dte.Assignments);
    }

    /// <summary>Final review I1 (Disconnect AI variant): no stray question after the user disconnected AI.</summary>
    [Fact]
    public async Task Disconnect_ai_while_preparing_never_prompts()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        s.Dte.StackGate = new ManualResetEventSlim(false);

        var call = Task.Run(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None));
        await s.Dte.StackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        s.Security.DisconnectAi();
        s.Dte.StackGate.Set();

        await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Empty(s.Prompt.Texts);
        Assert.Empty(s.Dte.Assignments);
    }

    /// <summary>Final review I2: consent covers the frame the question named; if the program moved, nothing changes.</summary>
    [Fact]
    public async Task Program_moving_while_the_question_is_open_voids_the_consent()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var call = s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Seq.Next();
        s.Prompt.Answer(ConsentChoice.AllowOnce);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal((int)BridgeErrorCode.WrongMode, ex.ErrorCode);
        Assert.Contains("changed", ex.Message, StringComparison.Ordinal);
        Assert.Empty(s.Dte.Assignments);
    }

    [Fact]
    public async Task Program_moving_voids_consent_for_side_effect_evaluation_too()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var call = s.Handler.EvaluateAsync("order.Touch()", allowSideEffects: true, SecurityHarness.Ctx(), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Seq.Next();
        s.Prompt.Answer(ConsentChoice.AllowOnce);

        await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Null(s.Dte.LastEvaluated);
    }

    /// <summary>Final review I3: a consented call that is then cancelled still leaves exactly one audit entry.</summary>
    [Fact]
    public async Task Cancelled_call_after_consent_is_audited_exactly_once()
    {
        using var s = new SecurityHarness();
        s.Dte.OnSetValue = (_, _) => throw new OperationCanceledException();
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx("c-000030"), CancellationToken.None));

        Assert.Single(s.AuditLines(), l => l.Contains("c-000030", StringComparison.Ordinal)
            && l.Contains("\"outcome\":\"cancelled\"", StringComparison.Ordinal)
            && l.Contains("\"decision\":\"allowOnce\"", StringComparison.Ordinal));
    }

    /// <summary>Final review I4: a conditional breakpoint that would be rejected never reaches the question.</summary>
    [Fact]
    public async Task Invalid_conditional_breakpoint_fails_before_prompting()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        var specs = new[]
        {
            new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "order.Id == " + new string('1', 600) },
            new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "order.Touch() > 0" },
            new BreakpointSpec { File = @"C:\elsewhere\Secret.cs", Line = 1, Condition = "x == 1" },
            new BreakpointSpec { File = "OrderService.cs", Line = 9, LogMessage = "{order.Touch()}" },
        };

        foreach (var spec in specs)
        {
            var ex = await Assert.ThrowsAsync<LocalRpcException>(() => s.Handler.SetBreakpointAsync(spec, SecurityHarness.Ctx(), CancellationToken.None));
            Assert.Equal((int)BridgeErrorCode.InvalidArgument, ex.ErrorCode);
        }

        Assert.Empty(s.Prompt.Texts);
        Assert.Empty(s.Dte.Bps);
    }

    [Fact]
    public async Task Handshake_registers_events_with_the_clients_minor()
    {
        var minors = new List<int>();
        var h = Create(onAuthenticated: minors.Add);

        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major, ProtocolMinor = 0 }, CancellationToken.None);

        Assert.Equal([0], minors);
    }

    [Fact]
    public async Task Consent_question_names_the_selected_frame()
    {
        using var s = new SecurityHarness();
        s.Dte.Frames =
        [
            new() { Function = "Demo.OrderService.Calculate(Demo.Order order)", File = @"C:\src\Demo\OrderService.cs", Line = 22, Language = "C#" },
            new() { Function = "Demo.OrderService.Submit(Demo.Order order)", File = @"C:\src\Demo\OrderService.cs", Line = 7, Language = "C#" },
        ];
        s.Dte.CurrentFrameIndex = 1;
        await s.HandshakeAsync("claude-code");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Equal("claude-code wants to set total = 0 in Demo.OrderService.Submit(Demo.Order order)", Assert.Single(s.Prompt.Texts));
    }

    private static async Task<string> ConditionalBreakpoint(SecurityHarness s)
    {
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        var r = await s.Handler.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9, Condition = "order.Id == 7" }, SecurityHarness.Ctx(), CancellationToken.None);
        s.Prompt.AutoAnswer = null;
        s.Prompt.Texts.Clear();
        return r.Breakpoint.Id;
    }

    [Fact]
    public async Task Enabling_a_conditional_breakpoint_asks_and_disabling_does_not()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var id = await ConditionalBreakpoint(s);
        s.Dte.Bps[0].Enabled = false;

        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;
        await s.Handler.SetBreakpointEnabledAsync(id, true, SecurityHarness.Ctx(), CancellationToken.None);
        Assert.Equal("claude-code wants to enable the breakpoint at OrderService.cs:9 when order.Id == 7 — this runs in your program on every hit", Assert.Single(s.Prompt.Texts));

        await s.Handler.SetBreakpointEnabledAsync(id, false, SecurityHarness.Ctx(), CancellationToken.None);
        Assert.Single(s.Prompt.Texts);
        Assert.False(s.Dte.Bps[0].Enabled);
    }

    [Fact]
    public async Task Enabling_a_plain_breakpoint_is_control_tier()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        var r = await s.Handler.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, SecurityHarness.Ctx(), CancellationToken.None);

        await s.Handler.SetBreakpointEnabledAsync(r.Breakpoint.Id, true, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Empty(s.Prompt.Texts);
    }

    [Fact]
    public async Task Enabling_an_unknown_breakpoint_returns_null_without_asking()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        Assert.Null(await s.Handler.SetBreakpointEnabledAsync("bp-00000000", true, SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Empty(s.Prompt.Texts);
    }

    /// <summary>Review focus #1.</summary>
    [Fact]
    public async Task Frame_change_while_the_question_is_open_voids_the_consent()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var call = s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Service.OnContextChanged();        // the user clicked another frame in the Call Stack window
        s.Prompt.Answer(ConsentChoice.AllowOnce);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal((int)BridgeErrorCode.WrongMode, ex.ErrorCode);
        Assert.Empty(s.Dte.Assignments);
    }

    /// <summary>
    /// The user drags the yellow arrow (Set Next Statement) while the question is open. Visual Studio may raise no event
    /// for that, so the approval is also bound to the line the question named.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Instruction_pointer_moved_while_the_question_is_open_voids_the_consent(bool setValue)
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();

        var call = setValue
            ? s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None)
            : s.Handler.EvaluateAsync("order.Touch()", allowSideEffects: true, SecurityHarness.Ctx(), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        s.Dte.Frames[0].Line = 20;           // moved to another line; no debugger event was raised
        s.Prompt.Answer(ConsentChoice.AllowOnce);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => call);
        Assert.Equal((int)BridgeErrorCode.WrongMode, ex.ErrorCode);
        Assert.Empty(s.Dte.Assignments);
        Assert.DoesNotContain("Evaluate:order.Touch()", s.Dte.Calls);
    }

    /// <summary>M2 review minor 1, end to end: Mutate changed to Allow while a request waited in the queue.</summary>
    [Fact]
    public async Task Queued_request_runs_when_the_user_allows_the_tier_meanwhile()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync();
        var first = s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx("c-000001"), CancellationToken.None);
        await s.Prompt.WaitForPromptsAsync(1);
        var second = s.Handler.SetValueAsync("total", "1", SecurityHarness.Ctx("c-000002"), CancellationToken.None);

        s.Settings.Permissions.Mutate = PermissionLevel.Allow;
        s.Prompt.Answer(ConsentChoice.Deny);

        await Assert.ThrowsAsync<LocalRpcException>(() => first);
        await second;
        Assert.Equal([("total", "1")], s.Dte.Assignments);
        Assert.Single(s.Prompt.Texts);
        Assert.Single(s.AuditLines(), l => l.Contains("c-000002", StringComparison.Ordinal) && l.Contains("\"decision\":\"allow\"", StringComparison.Ordinal));
    }

    /// <summary>M2 review minor 3: the client name was escaped twice ("a\b" showed as "a\\\\b").</summary>
    [Fact]
    public async Task Client_name_is_escaped_exactly_once()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("team\\bot");
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.SetValueAsync("total", "0", SecurityHarness.Ctx(), CancellationToken.None);

        Assert.StartsWith("team\\\\bot wants to set", Assert.Single(s.Prompt.Texts), StringComparison.Ordinal);
    }

    /// <summary>M2 review minor 4.</summary>
    [Fact]
    public async Task Assignment_rejected_by_visual_studio_is_audited_as_failed()
    {
        using var s = new SecurityHarness();
        s.Dte.OnSetValue = (_, _) => new EvaluateResult { IsValid = false, Value = "Visual Studio could not assign the value", Type = "decimal" };
        await s.HandshakeAsync();
        s.Prompt.AutoAnswer = ConsentChoice.AllowOnce;

        await s.Handler.SetValueAsync("total", "x", SecurityHarness.Ctx("c-000077"), CancellationToken.None);

        Assert.Single(s.AuditLines(), l => l.Contains("c-000077", StringComparison.Ordinal) && l.Contains("\"outcome\":\"failed\"", StringComparison.Ordinal));
    }

    /// <summary>M2 review minor 6: two handshakes racing on one connection registered it twice.</summary>
    [Fact]
    public async Task Concurrent_handshakes_register_the_connection_once()
    {
        for (var round = 0; round < 5; round++)
        {
            var ui = new GatedUiThread();
            var access = new AiAccessSwitch(enabled: true);
            access.Changed += (_, _) => Thread.Sleep(50);   // widens the window between the check and the registration
            var settings = new TestSettings();
            var security = new BridgeSecurity(settings, new ConsentGate(new FakePrompt(), () => settings.ConsentTimeout),
                new AuditLog(Path.Combine(Path.GetTempPath(), "debugviamcp-tests-audit"), () => 7, new NullActivity(), new NullLog()), access);
            var h = new BridgeConnectionHandler(new DebugBridgeService(new FakeDte(), ui, new EventSeq(), new NullLog(), "0.1.0"), "secret", security, new NullLog());
            var request = new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major, ClientName = "claude-code" };

            var a = h.HandshakeAsync(request, CancellationToken.None);
            var b = h.HandshakeAsync(request, CancellationToken.None);
            ui.Gate.SetResult();
            await Task.WhenAll(a, b);

            Assert.Single(access.Clients);
            h.Dispose();
            Assert.Empty(access.Clients);
        }
    }
}
