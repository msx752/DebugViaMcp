using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class McpSetupTests
{
    [Fact]
    public void The_copied_text_is_exactly_the_two_commands_one_per_line()
    {
        var lines = McpSetup.Commands.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal(2, lines.Length);
        Assert.Equal("dotnet tool install -g VsMcpDebugger --prerelease", lines[0]);
        Assert.Equal("claude mcp add vsmcpdebugger -- vsmcpdebugger serve", lines[1]);
    }

    [Fact]
    public void The_explanation_names_what_is_needed_and_what_to_do_next()
    {
        var text = McpSetup.Explanation;

        Assert.Contains(".NET 10 SDK", text, StringComparison.Ordinal);
        Assert.Contains("terminal", text, StringComparison.Ordinal);
        Assert.Contains("MCP client", text, StringComparison.Ordinal);
        Assert.DoesNotContain(McpSetup.Commands, text, StringComparison.Ordinal);   // the commands are on the clipboard, not repeated
    }

    [Fact]
    public void The_fallback_text_shows_the_commands_when_the_clipboard_is_unavailable()
    {
        Assert.Contains(McpSetup.Commands, McpSetup.ClipboardFailed, StringComparison.Ordinal);
    }
}
