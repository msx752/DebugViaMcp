namespace DebugViaMcp.Vsix.Core.Setup;

/// <summary>Wording of Extensions → Debugging via MCP → Copy MCP Setup Command.</summary>
public static class McpSetup
{
    /// <summary>What goes on the clipboard: the command that registers the extension's MCP server with Claude Code.</summary>
    public static string Commands(string launcherPath) => McpSetupPlan.RegisterCommand(launcherPath);

    public const string Title = "Debugging via MCP - MCP setup";

    public const string Explanation =
        "The setup command is on the clipboard.\n\n" +
        "Paste it into a terminal. It registers the MCP server that this extension installed with Claude Code " +
        "(the server needs the .NET 10 runtime). " +
        "Another MCP client needs its own registration of the same command.\n\n" +
        "Then start your agent in the folder of this solution. Debugging via MCP works with Visual Studio only, not with Visual Studio Code.";

    public static string ClipboardFailed(string launcherPath) =>
        "The clipboard is not available right now. Run this command in a terminal:\n\n" + Commands(launcherPath);
}
