using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Security;

namespace DebugViaMcp.Vsix.Core.Tests.Fakes;

public sealed class TestSettings : IBridgeSettings
{
    public PermissionSet Permissions { get; set; } = new();
    public TimeSpan ConsentTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public LogVerbosity LogLevel { get; set; } = LogVerbosity.Information;
    public int LogRetentionDays { get; set; } = 7;
    public int MaxWaitSeconds { get; set; } = 120;
    public int MaxBuildSeconds { get; set; } = 600;
}
