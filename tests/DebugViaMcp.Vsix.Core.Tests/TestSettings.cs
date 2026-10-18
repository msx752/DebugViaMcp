using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Core.Tests;

public sealed class TestSettings : IBridgeSettings
{
    public PermissionSet Permissions { get; set; } = new();
    public TimeSpan ConsentTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public LogVerbosity LogLevel { get; set; } = LogVerbosity.Information;
    public int LogRetentionDays { get; set; } = 7;
}
