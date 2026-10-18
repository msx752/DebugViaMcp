#:package ModelContextProtocol
#:property PublishAot=false
// M3.1 driver: two MCP servers (two agents) with different working directories against two running Visual Studios.
// Written and compile-checked only; the owner runs it. See docs/testing/m3.1-manual-e2e.md.
// Usage: dotnet run scripts/e2e/m3.1-two-instances.cs -- <path-to-DebugViaMcp.Mcp.dll> <folder A> <folder B>
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

if (args.Length < 3)
{
    Console.WriteLine("Usage: dotnet run scripts/e2e/m3.1-two-instances.cs -- <DebugViaMcp.Mcp.dll> <folder A> <folder B>");
    return 2;
}

var server = Path.GetFullPath(args[0]);

async Task<McpClient> StartAgent(string name, string folder) => await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = name,
    Command = "dotnet",
    Arguments = [server, "serve"],
    WorkingDirectory = Path.GetFullPath(folder),
}));

async Task<JsonElement> Call(McpClient client, string agent, string tool, Dictionary<string, object?>? arguments = null)
{
    var result = await client.CallToolAsync(tool, arguments ?? []);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
    Console.WriteLine($"== [{agent}] {tool} {string.Join(", ", (arguments ?? []).Select(kv => $"{kv.Key}={kv.Value}"))}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 3000 ? text[..3000] + " ..." : text);
    Console.WriteLine();
    return JsonDocument.Parse(text).RootElement.Clone();
}

await using var a = await StartAgent("agent-a", args[1]);
await using var b = await StartAgent("agent-b", args[2]);

var instances = await Call(a, "A", "vs_instances");
await Call(b, "B", "vs_instances");
await Call(a, "A", "debug_status");
await Call(b, "B", "debug_status");

// Agent B tries to use the Visual Studio that agent A chose and holds.
var first = instances.GetProperty("instances").EnumerateArray().FirstOrDefault(i => i.GetProperty("matchesFolder").GetBoolean());
if (first.ValueKind == JsonValueKind.Object)
{
    await Call(a, "A", "bp_list");
    await Call(a, "A", "vs_release");
    await Call(a, "A", "bp_set", new() { ["file"] = "Program.cs", ["line"] = 1 });   // takes the Visual Studio again
    var pid = first.GetProperty("pid").GetInt32();
    await Call(b, "B", "vs_select", new() { ["pid"] = pid });
    await Call(b, "B", "bp_set", new() { ["file"] = "Program.cs", ["line"] = 2 });    // expected: in_use
    await Call(a, "A", "vs_release");
}

return 0;
