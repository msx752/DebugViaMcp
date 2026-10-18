using System;

namespace DebugViaMcp.Vsix.Core;

/// <summary>Minimal logging abstraction; implementations must never receive variable values.</summary>
public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
