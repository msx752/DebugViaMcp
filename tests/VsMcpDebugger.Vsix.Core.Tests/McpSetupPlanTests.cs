using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class McpSetupPlanTests
{
    private static SetupState State(int? sdk = 10, string? tool = "0.4.0-preview.1", bool? claudeFound = true, bool? registered = true) =>
        new() { DotnetSdkMajor = sdk, ToolVersion = tool, ClaudeFound = claudeFound, ClaudeRegistered = registered, ExtensionVersion = "0.4.0" };

    [Fact]
    public void Everything_in_place_needs_no_steps()
    {
        var plan = McpSetupPlan.Create(State());

        Assert.Empty(plan.Steps);
        Assert.True(plan.IsComplete);
        Assert.All(plan.Lines, l => Assert.StartsWith("OK", l, StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_tool_is_installed_and_then_registered()
    {
        var plan = McpSetupPlan.Create(State(tool: null, registered: false));

        Assert.Equal([SetupStepKind.InstallTool, SetupStepKind.RegisterClaude], plan.Steps.Select(s => s.Kind));
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void A_tool_of_another_version_is_updated()
    {
        var plan = McpSetupPlan.Create(State(tool: "0.3.0"));

        Assert.Equal([SetupStepKind.UpdateTool], plan.Steps.Select(s => s.Kind));
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.4.0-preview.1")]
    [InlineData("0.4.0-preview.2")]
    public void A_tool_of_the_same_release_is_left_alone(string tool) =>
        Assert.Empty(McpSetupPlan.Create(State(tool: tool)).Steps);

    [Fact]
    public void Without_claude_code_the_tool_is_still_installed_but_nothing_is_registered()
    {
        var plan = McpSetupPlan.Create(State(tool: null, claudeFound: false, registered: null));

        Assert.Equal([SetupStepKind.InstallTool], plan.Steps.Select(s => s.Kind));
        Assert.Contains(plan.Lines, l => l.Contains("Claude Code was not found", StringComparison.Ordinal));
        Assert.Contains(plan.Lines, l => l.Contains("vsmcpdebugger serve", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_dotnet_10_sdk_nothing_is_run_and_the_user_is_told_what_to_install()
    {
        var plan = McpSetupPlan.Create(State(sdk: 8, tool: null, registered: false));

        Assert.Empty(plan.Steps);
        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Lines, l => l.Contains(".NET 10 SDK", StringComparison.Ordinal) && l.StartsWith("MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_answers_never_trigger_steps()
    {
        var plan = McpSetupPlan.Create(State(sdk: null, tool: null, claudeFound: null, registered: null));

        Assert.Empty(plan.Steps);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void The_commands_are_fixed_strings_without_any_user_input()
    {
        var all = McpSetupPlan.Create(State(tool: null, registered: false)).Steps.Concat(McpSetupPlan.Create(State(tool: "0.1.0")).Steps).ToArray();

        Assert.Contains(all, s => s.Display == "dotnet tool install -g VsMcpDebugger --prerelease");
        Assert.Contains(all, s => s.Display == "dotnet tool update -g VsMcpDebugger --prerelease");
        Assert.Contains(all, s => s.Display == "claude mcp add --scope user vsmcpdebugger -- vsmcpdebugger serve");
    }

    [Theory]
    [InlineData("  8.0.404 [C:\\Program Files\\dotnet\\sdk]\r\n  10.0.402 [C:\\Program Files\\dotnet\\sdk]\r\n", 10)]
    [InlineData("9.0.100 [/usr/share/dotnet/sdk]", 9)]
    [InlineData("", null)]
    [InlineData("no sdk here", null)]
    public void The_sdk_major_is_the_highest_listed(string output, int? expected) =>
        Assert.Equal(expected, McpSetupPlan.ParseSdkMajor(output));

    [Theory]
    [InlineData("Package Id         Version            Commands\r\n------------------------------------------------\r\nvsmcpdebugger      0.4.0-preview.1    vsmcpdebugger\r\n", "0.4.0-preview.1")]
    [InlineData("Package Id  Version  Commands\r\nsomething.else  1.0.0  x\r\n", null)]
    [InlineData("Paket Kimliği   Sürüm   Komutlar\r\n------\r\ndotnet-ef  10.0.9  dotnet-ef\r\nvsmcpdebugger   0.2.0   vsmcpdebugger  \r\n", "0.2.0")]
    [InlineData("VsMcpDebugger 0.3.0 vsmcpdebugger", "0.3.0")]
    [InlineData("", null)]
    public void The_tool_version_is_read_from_dotnet_tool_list(string output, string? expected) =>
        Assert.Equal(expected, McpSetupPlan.ParseToolVersion(output));

    [Theory]
    [InlineData("Checking MCP server health...\r\n\r\nvsmcpdebugger: vsmcpdebugger serve - Connected\r\nother: x - Connected", true)]
    [InlineData("No MCP servers configured.", false)]
    [InlineData("my-vsmcpdebugger-fork: x", false)]
    [InlineData("", false)]
    public void Registration_is_read_from_claude_mcp_list(string output, bool expected) =>
        Assert.Equal(expected, McpSetupPlan.ParseRegistered(output));
}
