using System;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix.Options;

/// <summary>
/// Reads the settings' current snapshot on every call (one reference read, safe off the UI thread), so a change takes
/// effect on the next request.
/// </summary>
internal sealed class OptionsBridgeSettings(McpSettings settings) : IBridgeSettings
{
    public PermissionSet Permissions
    {
        get
        {
            var p = settings.Applied.Permissions;
            return new PermissionSet { Read = p.Read, Control = p.Control, Mutate = p.Mutate };
        }
    }

    public TimeSpan ConsentTimeout => settings.Applied.ConsentTimeout;
    public LogVerbosity LogLevel => settings.Applied.LogLevel;
    public int LogRetentionDays => settings.Applied.LogRetentionDays;
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
