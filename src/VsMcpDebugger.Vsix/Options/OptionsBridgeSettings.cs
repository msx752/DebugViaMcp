using System;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Options;

/// <summary>
/// Reads the page's last applied snapshot on every call (one reference read, safe off the UI thread), so applied
/// changes take effect on the next request and values still being edited never do.
/// </summary>
internal sealed class OptionsBridgeSettings(GeneralOptionsPage page) : IBridgeSettings
{
    public PermissionSet Permissions
    {
        get
        {
            var p = page.Applied.Permissions;
            return new PermissionSet { Read = p.Read, Control = p.Control, Mutate = p.Mutate };
        }
    }

    public TimeSpan ConsentTimeout => page.Applied.ConsentTimeout;
    public LogVerbosity LogLevel => page.Applied.LogLevel;
    public int LogRetentionDays => page.Applied.LogRetentionDays;
}

/// <summary>Immutable copy of the applied Options values.</summary>
internal sealed class AppliedSettings(PermissionSet permissions, TimeSpan consentTimeout, bool acceptConnectionsOnStartup, bool showStatusBarIndicator, LogVerbosity logLevel, int logRetentionDays)
{
    public static readonly AppliedSettings Defaults = new(new PermissionSet(), TimeSpan.FromSeconds(60), true, true, LogVerbosity.Information, 7);

    public PermissionSet Permissions { get; } = permissions;
    public TimeSpan ConsentTimeout { get; } = consentTimeout;
    public bool AcceptConnectionsOnStartup { get; } = acceptConnectionsOnStartup;
    public bool ShowStatusBarIndicator { get; } = showStatusBarIndicator;
    public LogVerbosity LogLevel { get; } = logLevel;
    public int LogRetentionDays { get; } = logRetentionDays;
}
