using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Core.Tests;

/// <summary>Prompt that waits for the test to answer; counts how many are open at once and how many were dismissed.</summary>
public sealed class FakePrompt : IConsentPrompt
{
    private readonly object _gate = new();
    private readonly Queue<TaskCompletionSource<ConsentChoice>> _pending = new();
    private int _open;

    public List<string> Texts { get; } = [];
    public int MaxOpen { get; private set; }
    public int Dismissed { get; private set; }

    /// <summary>When set, every prompt is answered immediately with this choice.</summary>
    public ConsentChoice? AutoAnswer { get; set; }

    public async Task<ConsentChoice> AskAsync(string text, TimeSpan timeout, CancellationToken ct)
    {
        TaskCompletionSource<ConsentChoice> tcs;
        lock (_gate)
        {
            Texts.Add(text);
            _open++;
            MaxOpen = Math.Max(MaxOpen, _open);
            if (AutoAnswer is { } answer)
            {
                _open--;
                return answer;
            }

            tcs = new TaskCompletionSource<ConsentChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(tcs);
        }

        try
        {
            using (ct.Register(() => tcs.TrySetCanceled(ct)))
                return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate) Dismissed++;
            throw;
        }
        finally
        {
            lock (_gate) _open--;
        }
    }

    public void Answer(ConsentChoice choice)
    {
        TaskCompletionSource<ConsentChoice> tcs;
        lock (_gate) tcs = _pending.Dequeue();
        tcs.TrySetResult(choice);
    }

    public async Task WaitForPromptsAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (_gate)
            {
                if (Texts.Count >= count) return;
            }

            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} prompts.");
            await Task.Delay(10);
        }
    }
}

/// <summary>Timers that elapse only when the test says so.</summary>
public sealed class ManualDelay
{
    private readonly List<TaskCompletionSource<bool>> _timers = [];

    public int Count
    {
        get { lock (_timers) return _timers.Count; }
    }

    public Task Delay(TimeSpan duration, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        lock (_timers) _timers.Add(tcs);
        return tcs.Task;
    }

    public void Elapse(int index)
    {
        TaskCompletionSource<bool> tcs;
        lock (_timers) tcs = _timers[index];
        tcs.TrySetResult(true);
    }

    public async Task WaitForTimersAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Count < count)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} timers.");
            await Task.Delay(10);
        }
    }
}
