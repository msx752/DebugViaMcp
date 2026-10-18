using Microsoft.Extensions.Logging;

namespace DebugViaMcp.Mcp.Tests.Fakes;

/// <summary>Records formatted messages together with the innermost active scope.</summary>
public sealed class CapturingLogger : ILogger
{
    private readonly AsyncLocal<string?> _scope = new();

    public List<(string Message, string? Scope)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        var previous = _scope.Value;
        _scope.Value = state.ToString();
        return new Restore(() => _scope.Value = previous);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries) Entries.Add((formatter(state, exception), _scope.Value));
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
