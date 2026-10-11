using System.Threading.Tasks;

namespace VsMcpDebugger.Protocol;

public sealed class ModeChangedEvent
{
    public DebuggerMode Mode { get; set; }
    public long Seq { get; set; }
}

public sealed class BreakEvent
{
    public long Seq { get; set; }
    public BreakSnapshot Snapshot { get; set; } = new();
}

/// <summary>Notifications pushed from the VSIX to the MCP host. Sent as JSON-RPC notifications (no response).</summary>
public interface IDebugBridgeEvents
{
    Task OnModeChangedAsync(ModeChangedEvent e);
    Task OnBreakAsync(BreakEvent e);

    /// <summary>Sent just before the IDE closes the connection because the user chose Disconnect AI.</summary>
    Task OnAccessRevokedAsync();
}
