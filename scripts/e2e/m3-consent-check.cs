#:package ModelContextProtocol
#:property PublishAot=false
// Manual consent checks (docs/testing/m3-manual-e2e.md rows 5-6 and the instruction-pointer check).
// Stops OrderDemo in Calculate and opens ONE set_value question; the person at the keyboard acts in Visual Studio.
// Usage: dotnet run scripts/e2e/m3-consent-check.cs -- <path-to-VsMcpDebugger.Mcp.dll> <long|frame|ip>
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args[0];
var scenario = args[1];

// Run a private copy: the server must not lock its own output files.
var copy = Path.Combine(Path.GetTempPath(), "vsmcpdebugger-check-" + Environment.ProcessId);
Directory.CreateDirectory(copy);
foreach (var f in Directory.GetFiles(Path.GetDirectoryName(built)!)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);

await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "vsmcpdebugger",
    Command = "dotnet",
    Arguments = [Path.Combine(copy, Path.GetFileName(built)), "serve"],
}));

async Task<string> Call(string tool, Dictionary<string, object?>? arguments = null)
{
    var result = await client.CallToolAsync(tool, arguments ?? []);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
    Console.WriteLine($"== {tool}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 700 ? text[..700] + " …" : text);
    Console.WriteLine();
    return text;
}

await Call("bp_remove", new() { ["all"] = true });
await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 23 });
await Call("debug_start");

// 497 characters: close to the 500 limit, still a valid value.
var value = scenario == "long" ? "0" + string.Concat(Enumerable.Repeat(" + 0", 124)) : "0";
Console.WriteLine($"QUESTION OPEN ({scenario}): answer in Visual Studio within 60 s. Value length {value.Length}.");
Console.Out.Flush();

var answer = await Call("set_value", new() { ["expression"] = "sum", ["value"] = value });
Console.WriteLine($"RESULT ({scenario}): {(answer.Contains("\"error\"", StringComparison.Ordinal) ? "no change was made" : "value was assigned")}");

await Call("eval", new() { ["expression"] = "sum" });          // 0 only if the assignment ran
await Call("debug_stop");
await Call("bp_remove", new() { ["all"] = true });
