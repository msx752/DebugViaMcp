using System;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>Authorization state shared by every connection of one Visual Studio session.</summary>
public sealed class BridgeSecurity(IBridgeSettings settings, ConsentGate consent, AuditLog audit)
{
    public IBridgeSettings Settings { get; } = settings;
    public ConsentGate Consent { get; } = consent;
    public AuditLog Audit { get; } = audit;

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
