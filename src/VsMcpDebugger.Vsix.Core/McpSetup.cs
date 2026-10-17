namespace VsMcpDebugger.Vsix.Core;

/// <summary>Wording of Extensions → VsMcpDebugger → Copy MCP Setup Command.</summary>
public static class McpSetup
{
    /// <summary>What goes on the clipboard: install the MCP server, then register it with Claude Code.</summary>
    public const string Commands = "dotnet tool install -g VsMcpDebugger --prerelease\nclaude mcp add vsmcpdebugger -- vsmcpdebugger serve";

    public const string Title = "VsMcpDebugger - MCP setup";

    public const string Explanation =
        "The setup commands are on the clipboard.\n\n" +
        "Paste them into a terminal. They install the MCP server (needs the .NET 10 SDK) and register it with Claude Code. " +
        "Another MCP client needs its own registration of the command \"vsmcpdebugger serve\".\n\n" +
        "Open a new terminal afterwards so the tool is on your PATH, then start your agent in the folder of this solution.";

    public const string ClipboardFailed =
        "The clipboard is not available right now. Run these commands in a terminal:\n\n" + Commands;
}
