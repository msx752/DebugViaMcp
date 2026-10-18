using System;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix.Logging;

/// <summary>Honours Tools → Options → Log level for the extension's own log.</summary>
internal sealed class LevelFilterLog(ILog inner, Func<LogVerbosity> level) : ILog
{
    public void Info(string message)
    {
        if (level() >= LogVerbosity.Information) inner.Info(message);
    }

    public void Warn(string message)
    {
        if (level() >= LogVerbosity.Warning) inner.Warn(message);
    }

    public void Error(string message, Exception? exception = null) => inner.Error(message, exception);
}
