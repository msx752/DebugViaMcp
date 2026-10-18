using System.Text.Json;
using Xunit;
using DebugViaMcp.Vsix.Core.Setup;

namespace DebugViaMcp.Vsix.Core.Tests.Setup;

public class AgentClientsTests
{
    private const string Launcher = @"C:\Users\me\AppData\Local\DebugViaMcp\debugviamcp.cmd";

    private static AgentClient Client(string name, string launcher = Launcher) => AgentClients.For(launcher).Single(c => c.Name == name);

    [Fact]
    public void Several_clients_are_offered_and_none_is_only_claude()
    {
        var names = AgentClients.For(Launcher).Select(c => c.Name).ToArray();

        Assert.Contains("Claude Code", names);
        Assert.Contains("Codex", names);
        Assert.Contains("Cursor", names);
        Assert.Contains("Another MCP client", names);
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void Claude_code_gets_the_add_command_with_the_quoted_launcher()
    {
        Assert.Equal("claude mcp add --scope user debugviamcp -- \"" + Launcher + "\" serve", Client("Claude Code").Text);
    }

    [Fact]
    public void Codex_gets_a_command_and_a_toml_table()
    {
        var text = Client("Codex").Text;

        Assert.Contains("codex mcp add debugviamcp -- \"" + Launcher + "\" serve", text, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.debugviamcp]", text, StringComparison.Ordinal);
        Assert.Contains("command = \"C:\\\\Users\\\\me\\\\AppData\\\\Local\\\\DebugViaMcp\\\\debugviamcp.cmd\"", text, StringComparison.Ordinal);
        Assert.Contains("args = [\"serve\"]", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("Another MCP client")]
    public void The_json_is_valid_and_points_at_the_launcher(string name)
    {
        var text = Client(name).Text;
        var json = text.Substring(text.IndexOf('{', StringComparison.Ordinal));

        using var doc = JsonDocument.Parse(json);
        var server = doc.RootElement.GetProperty("mcpServers").GetProperty("debugviamcp");
        Assert.Equal(Launcher, server.GetProperty("command").GetString());
        Assert.Equal(["serve"], server.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToArray());
    }

    [Fact]
    public void A_path_with_quotes_and_backslashes_stays_valid_json()
    {
        var odd = "C:\\Users\\a \"b\"\\debugviamcp.cmd";
        var text = Client("Cursor", odd).Text;

        using var doc = JsonDocument.Parse(text);
        Assert.Equal(odd, doc.RootElement.GetProperty("mcpServers").GetProperty("debugviamcp").GetProperty("command").GetString());
    }

    [Fact]
    public void Every_entry_says_where_it_goes_and_names_the_server()
    {
        Assert.All(AgentClients.For(Launcher), c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Where));
            Assert.Contains("serve", c.Text, StringComparison.Ordinal);
        });
    }
}
