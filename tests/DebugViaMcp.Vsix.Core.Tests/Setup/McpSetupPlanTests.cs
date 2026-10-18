using Xunit;
using DebugViaMcp.Vsix.Core.Setup;

namespace DebugViaMcp.Vsix.Core.Tests.Setup;

public class McpSetupPlanTests
{
    private const string Launcher = @"C:\Users\me\AppData\Local\DebugViaMcp\debugviamcp.cmd";

    private static SetupState State(int? runtime = 10, bool deployed = true) =>
        new() { DotnetRuntimeMajor = runtime, ServerDeployed = deployed, LauncherPath = Launcher };

    [Fact]
    public void Everything_in_place_is_complete()
    {
        var plan = McpSetupPlan.Create(State());

        Assert.True(plan.IsComplete);
        Assert.All(plan.Lines, l => Assert.StartsWith("OK", l, StringComparison.Ordinal));
        Assert.Contains(plan.Lines, l => l.Contains(Launcher, StringComparison.Ordinal));
    }

    [Fact]
    public void A_server_that_could_not_be_installed_is_reported()
    {
        var plan = McpSetupPlan.Create(State(deployed: false));

        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Lines, l => l.StartsWith("MISSING", StringComparison.Ordinal) && l.Contains("could not install", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_dotnet_10_runtime_the_user_is_told_what_to_install()
    {
        var plan = McpSetupPlan.Create(State(runtime: 8));

        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Lines, l => l.Contains(".NET 10 runtime", StringComparison.Ordinal) && l.StartsWith("MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_runtime_is_not_complete()
    {
        var plan = McpSetupPlan.Create(State(runtime: null));

        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Lines, l => l.StartsWith("UNKNOWN", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.App 8.0.4 [C:\\x]\r\nMicrosoft.NETCore.App 8.0.4 [C:\\x]\r\nMicrosoft.NETCore.App 10.0.2 [C:\\x]\r\n", 10)]
    [InlineData("Microsoft.NETCore.App 9.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]", 9)]
    [InlineData("Microsoft.WindowsDesktop.App 10.0.2 [C:\\x]", null)]
    [InlineData("", null)]
    public void The_runtime_major_is_the_highest_netcore_runtime_listed(string output, int? expected) =>
        Assert.Equal(expected, McpSetupPlan.ParseRuntimeMajor(output));
}
