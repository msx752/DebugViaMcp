using System;
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix.Logging;

internal sealed class CompositeLog(params ILog[] logs) : ILog
{
    public void Info(string message) { foreach (var l in logs) l.Info(message); }
    public void Warn(string message) { foreach (var l in logs) l.Warn(message); }
    public void Error(string message, Exception? exception = null) { foreach (var l in logs) l.Error(message, exception); }
}
