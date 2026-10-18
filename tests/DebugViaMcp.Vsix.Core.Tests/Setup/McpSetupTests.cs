using Xunit;
using DebugViaMcp.Vsix.Core.Setup;

namespace DebugViaMcp.Vsix.Core.Tests.Setup;

public class McpSetupTests
{
    private const string Launcher = @"C:\Users\me\AppData\Local\DebugViaMcp\debugviamcp.cmd";

    [Fact]
    public void The_copied_text_is_the_registration_command_with_the_launcher_path()
    {
        Assert.Equal("claude mcp add --scope user debugviamcp -- \"" + Launcher + "\" serve", McpSetup.Commands(Launcher));
    }

    [Fact]
    public void The_explanation_names_what_is_needed_and_that_only_visual_studio_is_supported()
    {
        var text = McpSetup.Explanation;

        Assert.Contains(".NET 10 runtime", text, StringComparison.Ordinal);
        Assert.Contains("MCP client", text, StringComparison.Ordinal);
        Assert.Contains("Visual Studio only", text, StringComparison.Ordinal);
        Assert.DoesNotContain(McpSetup.Commands(Launcher), text, StringComparison.Ordinal);   // the command is on the clipboard, not repeated
    }

    [Fact]
    public void The_fallback_text_shows_the_command_when_the_clipboard_is_unavailable()
    {
        Assert.Contains(McpSetup.Commands(Launcher), McpSetup.ClipboardFailed(Launcher), StringComparison.Ordinal);
    }
}
