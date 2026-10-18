using System.Threading.Tasks;

namespace DebugViaMcp.Protocol;

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

/// <summary>Protocol 2.1. The selected thread or frame changed (by the agent or the user); sent only to clients with minor ≥ 1.</summary>
public sealed class FrameChangedEvent
{
    public long Seq { get; set; }
    public long Epoch { get; set; }
    public int? ThreadId { get; set; }
    public int FrameIndex { get; set; }
}

/// <summary>Notifications pushed from the VSIX to the MCP host. Sent as JSON-RPC notifications (no response).</summary>
public interface IDebugBridgeEvents
{
    Task OnModeChangedAsync(ModeChangedEvent e);
    Task OnBreakAsync(BreakEvent e);

    /// <summary>Protocol 2.1. Sent only to clients that announced minor ≥ 1 at handshake.</summary>
    Task OnFrameChangedAsync(FrameChangedEvent e);

    /// <summary>Sent just before the IDE closes the connection because the user chose Disconnect AI.</summary>
    Task OnAccessRevokedAsync();
}
