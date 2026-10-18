using System.Runtime.CompilerServices;
using System.Text.Json;
using ModelContextProtocol.Client;
using Xunit;

namespace DebugViaMcp.Mcp.Tests;

/// <summary>
/// Launches the real debugviamcp executable over stdio, like Claude Code does. DEBUGVIAMCP_HOME points at an
/// empty temp directory so the tests never see real IDE instances or write to the real log folder.
/// </summary>
public sealed class HostTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("debugviamcp-host-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); }
        catch (IOException) { }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string ServerDll => Path.Combine(AppContext.BaseDirectory, "DebugViaMcp.Mcp.dll");

    private StdioClientTransport Transport() => new(new StdioClientTransportOptions
    {
        Name = "debugviamcp",
        Command = "dotnet",
        Arguments = [ServerDll, "serve"],
        EnvironmentVariables = new Dictionary<string, string?> { ["DEBUGVIAMCP_LOG_LEVEL"] = "Verbose", ["DEBUGVIAMCP_HOME"] = _home },
    });

    [Fact]
    public async Task Exposes_exactly_the_m3_tool_set()
    {
        await using var client = await McpClient.CreateAsync(Transport());

        var names = (await client.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["bp_enable", "bp_list", "bp_remove", "bp_set", "debug_attach", "debug_continue", "debug_pause", "debug_run_to", "debug_start", "debug_status",
             "debug_step", "debug_stop", "debug_wait", "diag", "errors_list", "eval", "exception", "frame_select", "output_read", "set_value", "stack", "threads", "vars", "vs_build", "vs_instances", "vs_release", "vs_select"],
            names);
    }

    [Fact]
    public async Task Diag_works_without_visual_studio()
    {
        await using var client = await McpClient.CreateAsync(Transport());
        var result = await client.CallToolAsync("diag", new Dictionary<string, object?> { ["tail"] = 5 });
        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public async Task Mutating_tools_are_not_annotated_read_only()
    {
        await using var client = await McpClient.CreateAsync(Transport());
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name);

        Assert.True(tools["set_value"].ProtocolTool.Annotations?.DestructiveHint);
        Assert.NotEqual(true, tools["set_value"].ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.NotEqual(true, tools["eval"].ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task Verbose_logging_does_not_corrupt_stdout()
    {
        // With Verbose logging every request produces log lines; if any reached stdout the
        // MCP client would fail to parse the stream and these calls would throw.
        await using var client = await McpClient.CreateAsync(Transport());
        for (var i = 0; i < 3; i++)
        {
            await client.ListToolsAsync();
            var result = await client.CallToolAsync("vs_instances", new Dictionary<string, object?>());
            Assert.NotEqual(true, result.IsError);
        }
    }

    [Fact]
    public async Task Tool_schema_matches_golden_file()
    {
        await using var client = await McpClient.CreateAsync(Transport());
        var tools = (await client.ListToolsAsync())
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new { t.Name, t.Description, Schema = t.JsonSchema });
        var actual = JsonSerializer.Serialize(tools, Indented).ReplaceLineEndings("\n") + "\n";

        GoldenFile.AssertMatches("tools.json", actual);
    }
}

/// <summary>
/// Minimal golden-file assertion: compares against Snapshots/{name}.verified.*; on mismatch writes
/// Snapshots/{name}.received.* next to it and fails. Accept a change by renaming received → verified.
/// </summary>
internal static class GoldenFile
{
    public static void AssertMatches(string name, string actual, [CallerFilePath] string callerFile = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        Directory.CreateDirectory(dir);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var verified = Path.Combine(dir, $"{stem}.verified{ext}");
        var received = Path.Combine(dir, $"{stem}.received{ext}");

        var expected = File.Exists(verified) ? File.ReadAllText(verified).ReplaceLineEndings("\n") : null;
        if (expected == actual)
        {
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, actual);
        Assert.Fail(expected is null
            ? $"No golden file yet. Review {received} and rename it to {Path.GetFileName(verified)}."
            : $"Output differs from {verified}. Review {received}; rename it to accept the change.");
    }
}
