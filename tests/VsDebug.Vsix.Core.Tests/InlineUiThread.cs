using VsDebug.Vsix.Core;

namespace VsDebug.Vsix.Core.Tests;

public sealed class InlineUiThread : IUiThread
{
    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct) => Task.FromResult(work());
}

public sealed class NullLog : ILog
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
