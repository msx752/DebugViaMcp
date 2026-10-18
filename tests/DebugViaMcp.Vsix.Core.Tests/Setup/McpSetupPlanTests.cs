using Xunit;
using DebugViaMcp.Vsix.Core.Setup;

namespace DebugViaMcp.Vsix.Core.Tests.Setup;

public class McpSetupPlanTests
{
    private const string Launcher = @"C:\Users\me\AppData\Local\DebugViaMcp\debugviamcp.cmd";

    private static SetupState State(int? runtime = 10, bool deployed = true, bool? claudeFound = true, ClaudeRegistration registration = ClaudeRegistration.Current) =>
        new() { DotnetRuntimeMajor = runtime, ServerDeployed = deployed, LauncherPath = Launcher, ClaudeFound = claudeFound, Registration = registration };

    [Fact]
    public void Everything_in_place_needs_no_steps()
    {
        var plan = McpSetupPlan.Create(State());

        Assert.Empty(plan.Steps);
        Assert.True(plan.IsComplete);
        Assert.All(plan.Lines, l => Assert.StartsWith("OK", l, StringComparison.Ordinal));
    }

    [Fact]
    public void An_unregistered_server_is_registered_with_the_launcher()
    {
        var plan = McpSetupPlan.Create(State(registration: ClaudeRegistration.None));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(SetupStepKind.RegisterClaude, step.Kind);
        Assert.Equal("claude mcp add --scope user debugviamcp -- \"" + Launcher + "\" serve", step.Display);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void An_older_registration_is_removed_and_registered_again()
    {
        var plan = McpSetupPlan.Create(State(registration: ClaudeRegistration.Other));

        Assert.Equal([SetupStepKind.RemoveOldRegistration, SetupStepKind.RegisterClaude], plan.Steps.Select(s => s.Kind));
        Assert.False(plan.IsComplete);
    }

    [Theory]
    [InlineData(ClaudeRegistration.None)]
    [InlineData(ClaudeRegistration.Other)]
    public void Nothing_is_registered_while_the_server_is_not_installed(ClaudeRegistration registration)
    {
        var plan = McpSetupPlan.Create(State(deployed: false, registration: registration));

        Assert.Empty(plan.Steps);
        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Lines, l => l.StartsWith("MISSING", StringComparison.Ordinal) && l.Contains("could not install", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_claude_code_nothing_is_registered_and_the_command_for_other_clients_is_shown()
    {
        var plan = McpSetupPlan.Create(State(claudeFound: false, registration: ClaudeRegistration.Unknown));

        Assert.Empty(plan.Steps);
        Assert.Contains(plan.Lines, l => l.Contains("Claude Code was not found", StringComparison.Ordinal) && l.Contains(Launcher + "\" serve", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_dotnet_10_runtime_the_user_is_told_what_to_install()
    {
        var plan = McpSetupPlan.Create(State(runtime: 8));

        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Lines, l => l.Contains(".NET 10 runtime", StringComparison.Ordinal) && l.StartsWith("MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_answers_never_trigger_steps()
    {
        var plan = McpSetupPlan.Create(State(runtime: null, claudeFound: null, registration: ClaudeRegistration.Unknown));

        Assert.Empty(plan.Steps);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void The_commands_are_fixed_strings_without_any_user_input()
    {
        var all = McpSetupPlan.Create(State(registration: ClaudeRegistration.Other)).Steps;

        Assert.Contains(all, s => s.Display == "claude mcp remove --scope user debugviamcp");
        Assert.Contains(all, s => s.Display == "claude mcp add --scope user debugviamcp -- \"" + Launcher + "\" serve");
        Assert.All(all, s => Assert.Equal("cmd.exe", s.FileName));
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.App 8.0.4 [C:\\x]\r\nMicrosoft.NETCore.App 8.0.4 [C:\\x]\r\nMicrosoft.NETCore.App 10.0.2 [C:\\x]\r\n", 10)]
    [InlineData("Microsoft.NETCore.App 9.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]", 9)]
    [InlineData("Microsoft.WindowsDesktop.App 10.0.2 [C:\\x]", null)]
    [InlineData("", null)]
    public void The_runtime_major_is_the_highest_netcore_runtime_listed(string output, int? expected) =>
        Assert.Equal(expected, McpSetupPlan.ParseRuntimeMajor(output));

    [Theory]
    [InlineData("Checking MCP server health...\r\n\r\ndebugviamcp: \"C:\\Users\\me\\AppData\\Local\\DebugViaMcp\\debugviamcp.cmd\" serve - Connected\r\nother: x - Connected", ClaudeRegistration.Current)]
    [InlineData("debugviamcp: c:\\users\\me\\appdata\\local\\debugviamcp\\debugviamcp.cmd serve - Connected", ClaudeRegistration.Current)]
    [InlineData("debugviamcp: debugviamcp serve - Connected", ClaudeRegistration.Other)]
    [InlineData("No MCP servers configured.", ClaudeRegistration.None)]
    [InlineData("my-debugviamcp-fork: x", ClaudeRegistration.None)]
    [InlineData("", ClaudeRegistration.None)]
    public void Registration_is_read_from_claude_mcp_list(string output, ClaudeRegistration expected) =>
        Assert.Equal(expected, McpSetupPlan.ParseRegistration(output, Launcher));
}
