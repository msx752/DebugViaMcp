using System;
using System.Threading.Tasks;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>What the pipe host lets a connection handler do with its own connection.</summary>
public sealed class ConnectionHooks(Action onAuthenticated, Func<Task> revokeAndClose)
{
    /// <summary>Registers the connection for debugger events.</summary>
    public Action OnAuthenticated { get; } = onAuthenticated;

    /// <summary>Tells the client access was revoked, then closes the connection.</summary>
    public Func<Task> RevokeAndClose { get; } = revokeAndClose;
}
