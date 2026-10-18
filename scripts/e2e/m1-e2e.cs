#:package ModelContextProtocol
#:property PublishAot=false
// M1 end-to-end driver: talks to the real debugviamcp MCP server over stdio, exactly like Claude Code,
// against a running Visual Studio with the Debugging via MCP extension and samples/OrderDemo open.
// Usage: dotnet run scripts/e2e/m1-e2e.cs -- <path-to-DebugViaMcp.Mcp.dll>
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var server = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");
await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "debugviamcp",
    Command = "dotnet",
    Arguments = [server, "serve"],
}));

async Task<string> Call(string tool, Dictionary<string, object?>? arguments = null)
{
    var result = await client.CallToolAsync(tool, arguments ?? []);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    Console.WriteLine($"== {tool} {string.Join(", ", (arguments ?? []).Select(kv => $"{kv.Key}={kv.Value}"))}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 1500 ? text[..1500] + " …" : text);
    Console.WriteLine();
    return text;
}

await Call("vs_instances");
await Call("debug_status");
await Call("bp_remove", new() { ["all"] = true });
await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 7, ["condition"] = "order.Id == 7" });
await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 7, ["condition"] = "order.Touch() > 0" });
await Call("bp_list");
await Call("debug_start");
await Call("debug_wait", new() { ["timeoutSec"] = 60 });
await Call("vars", new() { ["scope"] = "args" });
await Call("debug_step", new() { ["kind"] = "over" });
await Call("eval", new() { ["expression"] = "total" });
await Call("vars", new() { ["scope"] = "args", ["path"] = "order.Lines" });
await Call("eval", new() { ["expression"] = "order.Touch()" });
await Call("eval", new() { ["expression"] = "order.TouchCount" });
await Call("eval", new() { ["expression"] = "order.Note" });
await Call("stack", new() { ["max"] = 5 });
await Call("debug_continue");
await Call("debug_stop");
await Call("bp_remove", new() { ["all"] = true });
await Call("debug_status");
