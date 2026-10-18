using System;
using System.Collections.Generic;

namespace DebugViaMcp.Vsix.Core.Setup;

/// <summary>One AI client and what the user has to put where to make it use the MCP server.</summary>
public sealed class AgentClient
{
    public AgentClient(string name, string where, string text)
    {
        Name = name;
        Where = where;
        Text = text;
    }

    public string Name { get; }

    /// <summary>One sentence: where the text goes.</summary>
    public string Where { get; }

    /// <summary>What to copy: a command or a configuration snippet. Fixed text around the launcher path; nothing an agent supplies.</summary>
    public string Text { get; }
}

/// <summary>The registration text for each supported AI client. The launcher path never changes with the extension version.</summary>
public static class AgentClients
{
    public static IReadOnlyList<AgentClient> For(string launcherPath)
    {
        if (launcherPath is null) throw new ArgumentNullException(nameof(launcherPath));
        var quoted = "\"" + launcherPath + "\"";
        var escaped = Escape(launcherPath);
        var json =
            "{\n" +
            "  \"mcpServers\": {\n" +
            "    \"debugviamcp\": {\n" +
            "      \"command\": \"" + escaped + "\",\n" +
            "      \"args\": [\"serve\"]\n" +
            "    }\n" +
            "  }\n" +
            "}";

        return
        [
            new AgentClient("Claude Code", "Run this in a terminal:",
                "claude mcp add --scope user debugviamcp -- " + quoted + " serve"),
            new AgentClient("Codex", "Run this in a terminal, or add the table below to %USERPROFILE%\\.codex\\config.toml:",
                "codex mcp add debugviamcp -- " + quoted + " serve\n\n" +
                "[mcp_servers.debugviamcp]\n" +
                "command = \"" + escaped + "\"\n" +
                "args = [\"serve\"]"),
            new AgentClient("Cursor", "Add this to %USERPROFILE%\\.cursor\\mcp.json (all projects) or to .cursor\\mcp.json in a project:", json),
            new AgentClient("Another MCP client", "Register a local (stdio) MCP server with this command and argument, or use the JSON if the client reads an \"mcpServers\" file:",
                "command:   " + launcherPath + "\nargument:  serve\n\n" + json),
        ];
    }

    // The path is the only variable part. Backslashes and quotes are what can occur in a Windows path.
    private static string Escape(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
