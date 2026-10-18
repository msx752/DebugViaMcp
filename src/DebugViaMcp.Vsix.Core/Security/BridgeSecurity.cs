using System;
using DebugViaMcp.Vsix.Core.Instances;

namespace DebugViaMcp.Vsix.Core.Security;

/// <summary>Authorization state shared by every connection of one Visual Studio session.</summary>
public sealed class BridgeSecurity(IBridgeSettings settings, ConsentGate consent, AuditLog audit, AiAccessSwitch access, InstanceLease? lease = null)
{
    /// <summary>Which connection holds this Visual Studio; shared by all connections of the session.</summary>
    public InstanceLease Lease { get; } = lease ?? new InstanceLease();

    public AiAccessSwitch Access { get; } = access;
    public IBridgeSettings Settings { get; } = settings;
    public ConsentGate Consent { get; } = consent;
    public AuditLog Audit { get; } = audit;

    public void DisconnectAi()
    {
        Access.Disconnect();
        Lease.Clear();
        Audit.Record(new AuditEntry { Ts = DateTimeOffset.UtcNow, Operation = "ai_disconnect", Outcome = "ok", Summary = "AI access disconnected by the user" });
    }

    public void ReconnectAi()
    {
        Access.Reconnect();
        Audit.Record(new AuditEntry { Ts = DateTimeOffset.UtcNow, Operation = "ai_reconnect", Outcome = "ok", Summary = "AI access re-enabled by the user" });
    }

    /// <summary>Called by the Options page after the user applied a change: audits it and drops all session grants.</summary>
    public void SettingsChanged(string summary)
    {
        Consent.ClearGrants();
        Audit.Record(new AuditEntry
        {
            Ts = DateTimeOffset.UtcNow, Operation = "settings", Outcome = "ok", Summary = summary,
            Params = new() { ["change"] = summary },
        });
    }
}
