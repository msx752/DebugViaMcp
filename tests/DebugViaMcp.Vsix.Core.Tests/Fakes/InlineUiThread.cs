using DebugViaMcp.Vsix.Core.Abstractions;


namespace DebugViaMcp.Vsix.Core.Tests.Fakes;

public sealed class InlineUiThread : IUiThread
{
    [ThreadStatic] private static bool _onUi;

    /// <summary>True while work passed to <see cref="RunAsync"/> is running, mimicking the VS UI thread.</summary>
    public static bool OnUiThread => _onUi;

    private readonly object _gate = new();

    /// <summary>Like the real UI thread, runs one piece of work at a time; work from other threads waits its turn.</summary>
    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        lock (_gate)
        {
            _onUi = true;
            try { return Task.FromResult(work()); }
            finally { _onUi = false; }
        }
    }
}

public sealed class NullLog : ILog
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>Holds every UI-thread call until the test opens the gate, then runs them concurrently on the thread pool.</summary>
public sealed class GatedUiThread : IUiThread
{
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        await Gate.Task.ConfigureAwait(false);
        return work();
    }
}
