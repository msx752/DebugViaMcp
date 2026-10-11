using System.Threading.Tasks;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Turns Visual Studio's OnBuildBegin/OnBuildDone events into an awaitable completion, so vs_build can wait for a
/// build it started or join one the user started.
/// </summary>
public sealed class BuildMonitor
{
    private readonly object _gate = new();
    private TaskCompletionSource<int>? _running;

    /// <summary>Completes with the number of failed projects when the running (or next) build finishes.</summary>
    public Task<int> Current
    {
        get { lock (_gate) return (_running ??= New()).Task; }
    }

    public void OnBuildBegin()
    {
        lock (_gate) _running ??= New();
    }

    public void OnBuildDone(int failedProjects)
    {
        TaskCompletionSource<int>? done;
        lock (_gate)
        {
            done = _running;
            _running = null;
        }

        done?.TrySetResult(failedProjects);
    }

    private static TaskCompletionSource<int> New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
