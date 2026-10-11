using VsDebug.Vsix.Core;

namespace VsDebug.Vsix.Core.Tests;

public sealed class InlineUiThread : IUiThread
{
    [ThreadStatic] private static bool _onUi;

    /// <summary>True while work passed to <see cref="RunAsync"/> is running, mimicking the VS UI thread.</summary>
    public static bool OnUiThread => _onUi;

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        _onUi = true;
        try { return Task.FromResult(work()); }
        finally { _onUi = false; }
    }
}

public sealed class NullLog : ILog
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
