#:package ModelContextProtocol
#:property PublishAot=false
// M3 end-to-end driver: talks to the real vsmcpdebugger MCP server over stdio, like Claude Code, against a running
// Visual Studio with the VsMcpDebugger 0.3 extension and samples/OrderDemo open (OrderDemo = startup project).
// Consent info bars are answered by scripts/e2e/click-infobar.ps1 (UI Automation).
// Usage: dotnet run scripts/e2e/m3-e2e.cs -- <path-to-VsMcpDebugger.Mcp.dll>
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args.Length > 0 ? args[0] : Path.GetFullPath("src/VsMcpDebugger.Mcp/bin/Debug/net10.0/VsMcpDebugger.Mcp.dll");

// Run a private copy: vs_build may build this very project, and a running server locks its own output files.
var copy = Path.Combine(Path.GetTempPath(), "vsmcpdebugger-e2e-" + Environment.ProcessId);
Directory.CreateDirectory(copy);
foreach (var f in Directory.GetFiles(Path.GetDirectoryName(built)!)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);
var server = Path.Combine(copy, Path.GetFileName(built));
var clicker = Path.GetFullPath("scripts/e2e/click-infobar.ps1");
var faultDemo = Path.GetFullPath("samples/FaultDemo/bin/Debug/net10.0/FaultDemo.exe");
await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "vsmcpdebugger",
    Command = "dotnet",
    Arguments = [server, "serve"],
}));

async Task<JsonElement> Call(string tool, Dictionary<string, object?>? arguments = null, string? click = null)
{
    var pending = client.CallToolAsync(tool, arguments ?? []).AsTask();
    if (click is not null)
    {
        using var p = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -File \"{clicker}\" -Button \"{click}\"") { UseShellExecute = false })!;
        await p.WaitForExitAsync();
        Console.WriteLine($"   (clicked '{click}', exit {p.ExitCode})");
    }

    var result = await pending;
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
    Console.WriteLine($"== {tool} {string.Join(", ", (arguments ?? []).Select(kv => $"{kv.Key}={kv.Value}"))}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 3000 ? text[..3000] + " …" : text);
    Console.WriteLine();
    return JsonDocument.Parse(text).RootElement.Clone();
}

Process StartFaultDemo(string arguments) =>
    Process.Start(new ProcessStartInfo(faultDemo, arguments) { UseShellExecute = false, CreateNoWindow = true })!;

using (var b = Process.Start(new ProcessStartInfo("dotnet", "build samples/FaultDemo -v q") { UseShellExecute = false })!) await b.WaitForExitAsync();

// IDE state
await Call("vs_instances");
await Call("debug_status");
await Call("vs_build", new() { ["project"] = "OrderDemo" });                                    // VS may have the whole .slnx open: build only the sample
await Call("errors_list", new() { ["severity"] = "error,warning,message" });
await Call("output_read");                                                                   // pane list: no VsMcpDebugger panes
await Call("output_read", new() { ["pane"] = "build", ["tail"] = 20 });
await Call("output_read", new() { ["pane"] = "VsMcpDebugger Activity" });                    // denied / policy

// Frames, threads, run to
await Call("bp_remove", new() { ["all"] = true });
await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 23 });   // inside Calculate
var start = await Call("debug_start");
var epoch = start.GetProperty("snapshot").GetProperty("epoch").GetInt64();
await Call("threads");
await Call("stack");
await Call("frame_select", new() { ["index"] = 1, ["epoch"] = epoch });                      // Submit
await Call("vars", new() { ["scope"] = "args" });                                            // frame.index = 1
await Call("eval", new() { ["expression"] = "order.Id" });
await Call("bp_remove", new() { ["all"] = true });
await Call("debug_run_to", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 14 });   // reason runTo
await Call("frame_select", new() { ["index"] = 0, ["epoch"] = epoch });                      // stale
await Call("bp_list");                                                                       // run-to breakpoint not listed

// bp_enable
var bp = await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 8, ["condition"] = "order.Id == 9" }, click: "Allow once");
var id = bp.GetProperty("breakpoint").GetProperty("id").GetString();
await Call("bp_enable", new() { ["id"] = id, ["enabled"] = false });                         // no info bar
await Call("bp_enable", new() { ["id"] = id, ["enabled"] = true }, click: "Allow once");     // info bar
await Call("debug_continue");                                                                // break at order 9
await Call("debug_stop");
await Call("bp_remove", new() { ["all"] = true });

// Attach and detach
using (var loop = StartFaultDemo("loop"))
{
    await Task.Delay(2000);
    await Call("debug_attach", new() { ["processName"] = "FaultDemo" }, click: "Allow once");
    await Call("bp_set", new() { ["function"] = "FaultDemo.Ticker.Work" });
    await Call("debug_wait", new() { ["timeoutSec"] = 30 });
    await Call("stack");
    await Call("bp_remove", new() { ["all"] = true });
    await Call("debug_stop");                                                                // detaches
    await Task.Delay(1500);
    Console.WriteLine($"   FaultDemo still running after detach: {!loop.HasExited}");
    loop.Kill();
}

// Exception
using (var crash = StartFaultDemo("crash 12"))
{
    await Task.Delay(2000);
    await Call("debug_attach", new() { ["processName"] = "FaultDemo" }, click: "Allow once");
    await Call("debug_wait", new() { ["timeoutSec"] = 60 });                                 // unhandled InvalidOperationException
    await Call("exception");                                                                 // inner ArgumentException
    await Call("debug_stop");
    if (!crash.HasExited) crash.Kill();
}

await Call("diag", new() { ["tail"] = 5 });
