using System;
using System.Threading.Tasks;

namespace DebugViaMcp.Vsix.Core.Bridge;

/// <summary>What the pipe host lets a connection handler do with its own connection.</summary>
public sealed class ConnectionHooks(Action<int> onAuthenticated, Func<Task> revokeAndClose, int connectionId = 0)
{
    /// <summary>Identifies this connection among those of one Visual Studio (the lease holder is named by it).</summary>
    public int ConnectionId { get; } = connectionId;

    /// <summary>Registers the connection for debugger events; the argument is the client's protocol minor.</summary>
    public Action<int> OnAuthenticated { get; } = onAuthenticated;

    /// <summary>Tells the client access was revoked, then closes the connection.</summary>
    public Func<Task> RevokeAndClose { get; } = revokeAndClose;
}
