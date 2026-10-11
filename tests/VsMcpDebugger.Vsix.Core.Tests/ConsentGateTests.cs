using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class ConsentGateTests
{
    private readonly FakePrompt _prompt = new();
    private readonly ManualDelay _delay = new();

    private ConsentGate Gate() => new(_prompt, () => TimeSpan.FromSeconds(60), _delay.Delay);

    [Fact]
    public async Task Allow_once_does_not_carry_over()
    {
        var gate = Gate();
        using var session = gate.CreateSession();
        _prompt.AutoAnswer = ConsentChoice.AllowOnce;

        Assert.Equal(ConsentOutcome.AllowedOnce, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Equal(ConsentOutcome.AllowedOnce, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Equal(2, _prompt.Texts.Count);
    }

    [Fact]
    public async Task Allow_for_session_covers_only_the_same_operation()
    {
        var gate = Gate();
        using var session = gate.CreateSession();
        _prompt.AutoAnswer = ConsentChoice.AllowSession;

        Assert.Equal(ConsentOutcome.AllowedForSession, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Equal(ConsentOutcome.AllowedBySessionGrant, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Single(_prompt.Texts);

        Assert.Equal(ConsentOutcome.AllowedForSession, await gate.RequestAsync(session, Operation.EvaluateWithSideEffects, "t", CancellationToken.None));
        Assert.Equal(2, _prompt.Texts.Count);
    }

    [Fact]
    public async Task Session_grants_do_not_leak_to_other_connections()
    {
        var gate = Gate();
        using var a = gate.CreateSession();
        using var b = gate.CreateSession();
        _prompt.AutoAnswer = ConsentChoice.AllowSession;

        await gate.RequestAsync(a, Operation.SetValue, "t", CancellationToken.None);
        await gate.RequestAsync(b, Operation.SetValue, "t", CancellationToken.None);

        Assert.Equal(2, _prompt.Texts.Count);
    }

    [Fact]
    public async Task Deny_is_denied()
    {
        var gate = Gate();
        using var session = gate.CreateSession();
        _prompt.AutoAnswer = ConsentChoice.Deny;

        Assert.Equal(ConsentOutcome.Denied, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
    }

    [Fact]
    public async Task No_answer_times_out_and_dismisses_the_prompt()
    {
        var gate = Gate();
        using var session = gate.CreateSession();

        var pending = gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None);
        await _prompt.WaitForPromptsAsync(1);
        _delay.Elapse(0);

        Assert.Equal(ConsentOutcome.TimedOut, await pending);
        Assert.Equal(1, _prompt.Dismissed);
    }

    /// <summary>Review focus #3.</summary>
    [Fact]
    public async Task Caller_cancellation_dismisses_prompt()
    {
        var gate = Gate();
        using var session = gate.CreateSession();
        using var cts = new CancellationTokenSource();

        var pending = gate.RequestAsync(session, Operation.SetValue, "t", cts.Token);
        await _prompt.WaitForPromptsAsync(1);
        cts.Cancel();

        Assert.Equal(ConsentOutcome.Cancelled, await pending);
        Assert.Equal(1, _prompt.Dismissed);
    }

    [Fact]
    public async Task Revoke_denies_pending_requests_and_clears_grants()
    {
        var gate = Gate();
        using var session = gate.CreateSession();
        _prompt.AutoAnswer = ConsentChoice.AllowSession;
        await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None);
        _prompt.AutoAnswer = null;

        var pending = gate.RequestAsync(session, Operation.EvaluateWithSideEffects, "t", CancellationToken.None);
        await _prompt.WaitForPromptsAsync(2);
        session.Revoke();

        Assert.Equal(ConsentOutcome.Disconnected, await pending);
        _prompt.AutoAnswer = ConsentChoice.AllowOnce;
        Assert.Equal(ConsentOutcome.Disconnected, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Equal(2, _prompt.Texts.Count);   // a revoked session never asks again
    }

    [Fact]
    public async Task Clear_grants_affects_every_session()
    {
        var gate = Gate();
        using var session = gate.CreateSession();
        _prompt.AutoAnswer = ConsentChoice.AllowSession;
        await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None);

        gate.ClearGrants();

        Assert.Equal(ConsentOutcome.AllowedForSession, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Equal(2, _prompt.Texts.Count);
    }

    [Fact]
    public async Task Only_one_prompt_is_open_at_a_time()
    {
        var gate = Gate();
        using var session = gate.CreateSession();

        var first = gate.RequestAsync(session, Operation.SetValue, "first", CancellationToken.None);
        var second = gate.RequestAsync(session, Operation.EvaluateWithSideEffects, "second", CancellationToken.None);
        await _prompt.WaitForPromptsAsync(1);
        await Task.Delay(50);
        Assert.Single(_prompt.Texts);

        _prompt.Answer(ConsentChoice.AllowOnce);
        await _prompt.WaitForPromptsAsync(2);
        _prompt.Answer(ConsentChoice.Deny);

        Assert.Equal(ConsentOutcome.AllowedOnce, await first);
        Assert.Equal(ConsentOutcome.Denied, await second);
        Assert.Equal(1, _prompt.MaxOpen);
    }

    /// <summary>A prompt whose answer can land after the request was already cancelled, like a UI one beat late.</summary>
    private sealed class LatePrompt : IConsentPrompt
    {
        public TaskCompletionSource<ConsentChoice> Answer { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Shown { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the gate cancels the question (timeout, caller, revoke).</summary>
        public TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get; private set; }

        public Task<ConsentChoice> AskAsync(string text, TimeSpan timeout, CancellationToken ct)
        {
            Count++;
            ct.Register(() => Cancelled.TrySetResult());
            Shown.TrySetResult();
            return Answer.Task;
        }

        public void Reset(ConsentChoice? next = null)
        {
            Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (next is { } n) Answer.SetResult(n);
        }
    }

    /// <summary>Security review: an Allow that lands after Disconnect AI must neither allow nor grant.</summary>
    [Fact]
    public async Task Answer_arriving_after_revoke_is_not_allowed_and_grants_nothing()
    {
        var late = new LatePrompt();
        var gate = new ConsentGate(late, () => TimeSpan.FromSeconds(60), _delay.Delay);
        using var session = gate.CreateSession();

        var pending = gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None);
        await late.Shown.Task;
        session.Revoke();
        await late.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        late.Answer.SetResult(ConsentChoice.AllowSession);

        Assert.Equal(ConsentOutcome.Disconnected, await pending);
        late.Reset(ConsentChoice.AllowOnce);
        Assert.Equal(ConsentOutcome.Disconnected, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
        Assert.Equal(1, late.Count);
    }

    /// <summary>Security review: an Allow that lands after the timeout must not count.</summary>
    [Fact]
    public async Task Answer_arriving_after_timeout_is_timed_out()
    {
        var late = new LatePrompt();
        var gate = new ConsentGate(late, () => TimeSpan.FromSeconds(60), _delay.Delay);
        using var session = gate.CreateSession();

        var pending = gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None);
        await late.Shown.Task;
        _delay.Elapse(0);
        await late.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        late.Answer.SetResult(ConsentChoice.AllowOnce);

        Assert.Equal(ConsentOutcome.TimedOut, await pending);
    }

    /// <summary>Security review: a settings change while the question is open must not leave a session grant behind.</summary>
    [Fact]
    public async Task Settings_change_during_prompt_allows_this_call_but_grants_nothing()
    {
        var late = new LatePrompt();
        var gate = new ConsentGate(late, () => TimeSpan.FromSeconds(60), _delay.Delay);
        using var session = gate.CreateSession();

        var pending = gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None);
        await late.Shown.Task;
        gate.ClearGrants();
        late.Answer.SetResult(ConsentChoice.AllowSession);

        Assert.Equal(ConsentOutcome.AllowedOnce, await pending);
        late.Reset(ConsentChoice.Deny);
        Assert.Equal(ConsentOutcome.Denied, await gate.RequestAsync(session, Operation.SetValue, "t", CancellationToken.None));
    }

    /// <summary>Review focus #1: the queued request's 60 s run while it waits.</summary>
    [Fact]
    public async Task Queued_request_times_out_while_waiting_and_never_prompts()
    {
        var gate = Gate();
        using var session = gate.CreateSession();

        var first = gate.RequestAsync(session, Operation.SetValue, "first", CancellationToken.None);
        await _prompt.WaitForPromptsAsync(1);
        var second = gate.RequestAsync(session, Operation.EvaluateWithSideEffects, "second", CancellationToken.None);
        await _delay.WaitForTimersAsync(2);
        _delay.Elapse(1);

        Assert.Equal(ConsentOutcome.TimedOut, await second);
        _prompt.Answer(ConsentChoice.AllowOnce);
        Assert.Equal(ConsentOutcome.AllowedOnce, await first);
        Assert.Single(_prompt.Texts);
    }
}
