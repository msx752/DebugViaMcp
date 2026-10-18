#:package ModelContextProtocol
#:property PublishAot=false
// M2 end-to-end driver: talks to the real debugviamcp MCP server over stdio, like Claude Code, against a running
// Visual Studio with the Debugging via MCP extension and samples/OrderDemo open (OrderDemo = startup project).
// Consent info bars are answered by tests/e2e/click-infobar.ps1 (UI Automation).
// Usage: dotnet run tests/e2e/safety-and-consent.cs -- <path-to-DebugViaMcp.Mcp.dll>
using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var server = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");
var clicker = Path.GetFullPath("tests/e2e/click-infobar.ps1");
await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "debugviamcp",
    Command = "dotnet",
    Arguments = [server, "serve"],
}));

async Task<string> Call(string tool, Dictionary<string, object?>? arguments = null, string? click = null)
{
    var pending = client.CallToolAsync(tool, arguments ?? []).AsTask();
    if (click is not null)
    {
        using var p = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -File \"{clicker}\" -Button \"{click}\"") { UseShellExecute = false })!;
        await p.WaitForExitAsync();
        Console.WriteLine($"   (clicked '{click}', exit {p.ExitCode})");
    }

    var result = await pending;
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    Console.WriteLine($"== {tool} {string.Join(", ", (arguments ?? []).Select(kv => $"{kv.Key}={kv.Value}"))}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 1500 ? text[..1500] + " …" : text);
    Console.WriteLine();
    return text;
}

// Runs an Extensions → Debugging via MCP command in the running Visual Studio through its automation object.
async Task VsCommand(string name)
{
    var script = "$dte = [Runtime.InteropServices.Marshal]::GetActiveObject('VisualStudio.DTE.18.0'); $dte.ExecuteCommand('" + name + "')";
    using var p = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"") { UseShellExecute = false })!;
    await p.WaitForExitAsync();
    Console.WriteLine($"   (Visual Studio command {name}, exit {p.ExitCode})");
}

await Call("debug_status");                                                                   // permissions: mutate = ask
await Call("bp_remove", new() { ["all"] = true });
await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 8, ["condition"] = "order.Id == 7" },
    click: "Allow for session");                                                              // condition runs on every hit → consent
await Call("debug_start");                                                                    // break at line 8, total = 1750
await Call("set_value", new() { ["expression"] = "total", ["value"] = "0" }, click: "Allow once");
await Call("eval", new() { ["expression"] = "total" });                                       // 0
await Call("set_value", new() { ["expression"] = "total", ["value"] = "5" }, click: "Deny");  // denied / user_denied
await Call("eval", new() { ["expression"] = "order.Touch()", ["allowSideEffects"] = true }, click: "Allow for session"); // 1
await Call("eval", new() { ["expression"] = "order.Touch()", ["allowSideEffects"] = true }); // 2, no prompt
await Call("set_value", new() { ["expression"] = "GetType().Name", ["value"] = "\"x\"" });    // invalid_argument, no prompt
await Call("set_value", new() { ["expression"] = "total", ["value"] = "1" });                 // no click: denied / timeout after 60 s
await VsCommand("DebugViaMcp.DisconnectAI");                                           // same as Ctrl+Alt+Shift+F12
await Call("debug_status");                                                                   // ai_disabled
await VsCommand("DebugViaMcp.ReconnectAI");
await Call("debug_status");                                                                   // reconnects: break
await Call("debug_continue");                                                                 // Console: "Order 7 accepted: 0"
await Call("debug_stop");
await Call("bp_remove", new() { ["all"] = true });
