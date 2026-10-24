#:package ModelContextProtocol
#:property PublishAot=false
// Watch list end-to-end driver: talks to the real debugviamcp MCP server over stdio, like Claude Code, against a running
// Visual Studio with the Debugging via MCP extension (protocol 2.9) and the repository's solution open (OrderDemo = startup project).
// Run it through tests/e2e/watch.ps1, which starts and closes Visual Studio. Nothing here answers a consent question
// (Read = Allow and Control = Allow, the defaults).
//
// Usage: dotnet run tests/e2e/watch.cs -- <path-to-DebugViaMcp.Mcp.dll>
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");

// Run a private copy: vs_build may build this very project, and a running server locks its own output files.
var copy = Path.Combine(Path.GetTempPath(), "debugviamcp-e2e-watch-" + Environment.ProcessId);
Directory.CreateDirectory(copy);
foreach (var f in Directory.GetFiles(Path.GetDirectoryName(built)!)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);

var failures = new List<string>();
void Check(string what, bool ok)
{
    Console.WriteLine($"   [{(ok ? "PASS" : "FAIL")}] {what}");
    if (!ok) failures.Add(what);
}

McpClient? agent = null;
try
{
    agent = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "debugviamcp", Command = "dotnet", Arguments = [Path.Combine(copy, Path.GetFileName(built)), "serve"],
    }));

    async Task<JsonElement> Call(string tool, Dictionary<string, object?>? a = null)
    {
        var r = await agent.CallToolAsync(tool, a ?? []);
        var text = r.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        Console.WriteLine($"== {tool} {string.Join(", ", (a ?? []).Select(kv => $"{kv.Key}={kv.Value}"))}{(r.IsError == true ? "  [ERROR]" : "")}");
        Console.WriteLine(text.Length > 900 ? text[..900] + " ..." : text);
        Console.WriteLine();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    static string? Prop(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString() : null;
    static JsonElement? Watch(JsonElement answer, string expression) =>
        answer.TryGetProperty("watches", out var list)
            ? list.EnumerateArray().Select(w => (JsonElement?)w).FirstOrDefault(w => Prop(w!.Value, "expression") == expression)
            : null;
    static string? ErrorCode(JsonElement answer) => answer.TryGetProperty("error", out var e) ? Prop(e, "code") : null;

    // debug_stop answers before the mode change reaches the server: wait for it, as an agent would, before the next start.
    async Task WaitForDesignAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            var st = await agent.CallToolAsync("debug_status", new Dictionary<string, object?>());
            if ((st.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "").Contains("\"mode\":\"design\"", StringComparison.Ordinal)) return;
            await Task.Delay(500);
        }
    }

    Console.WriteLine("##### watch list");
    var instances = await Call("vs_instances");
    Check("a Visual Studio instance is found", instances.GetProperty("instances").GetArrayLength() > 0);
    var status = await Call("debug_status");
    if (Prop(status.GetProperty("permissions"), "read") != "allow" || Prop(status.GetProperty("permissions"), "control") != "allow")
    {
        Check("Read and Control are Allow (the defaults); this driver answers no question", false);
        return 1;
    }

    await Call("bp_remove", new() { ["all"] = true });
    await Call("watch_remove", new() { ["all"] = true });

    // Design mode: entries are stored, values come at the next stop.
    var addSum = await Call("watch_add", new() { ["expression"] = "sum" });
    Check("watch_add outside a break stores the entry and says the value comes at the next stop", Prop(addSum.GetProperty("watch"), "id") == "w1" && Prop(addSum, "hint") is { } h && h.Contains("next stop"));
    await Call("watch_add", new() { ["expression"] = "line.Quantity", ["name"] = "quantity" });
    await Call("watch_add", new() { ["expression"] = "order.Lines.Count" });
    await Call("watch_add", new() { ["expression"] = "nope.missing" });
    var again = await Call("watch_add", new() { ["expression"] = "sum" });
    Check("the same expression again is the same entry", Prop(again, "alreadyExisted") == "True" && Prop(again.GetProperty("watch"), "id") == "w1");
    var unsafeAdd = await Call("watch_add", new() { ["expression"] = "a; b" });
    Check("an unsafe expression is refused", ErrorCode(unsafeAdd) == "invalid_argument");
    var inListBefore = await Call("watch_list");
    Check("four entries are on the list", inListBefore.GetProperty("watches").GetArrayLength() == 4);

    // A stop carries the values.
    await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 23 });
    var first = await Call("debug_start");
    Check("debug_start stops at the breakpoint", Prop(first, "state") == "break");
    Check("the stop carries sum", Prop(Watch(first, "sum")!.Value, "value") == "0");
    Check("the stop carries line.Quantity with its label", Prop(Watch(first, "line.Quantity")!.Value, "name") == "quantity" && Prop(Watch(first, "line.Quantity")!.Value, "value") == "1");
    Check("the stop carries order.Lines.Count", Prop(Watch(first, "order.Lines.Count")!.Value, "value") == "2");
    Check("an expression that cannot be read has its own error, the others still come", Prop(Watch(first, "nope.missing")!.Value, "error") is { Length: > 0 } && Prop(Watch(first, "nope.missing")!.Value, "value") is null);
    Check("nothing is marked changed at the first stop", first.GetProperty("watches").EnumerateArray().All(w => Prop(w, "changed") != "True"));

    var second = await Call("debug_continue");
    Check("the next stop reports sum as changed", Prop(Watch(second, "sum")!.Value, "changed") == "True" && Prop(Watch(second, "sum")!.Value, "value") != "0");
    Check("order.Lines.Count did not change", Prop(Watch(second, "order.Lines.Count")!.Value, "changed") != "True");

    var statusAtBreak = await Call("debug_status");
    Check("debug_status at a break carries the values too", statusAtBreak.TryGetProperty("watches", out _));
    var step = await Call("debug_step", new() { ["kind"] = "over" });
    Check("debug_step carries the values", step.TryGetProperty("watches", out _));

    // Removal.
    var gone = await Call("watch_remove", new() { ["id"] = "w4" });
    Check("watch_remove by id", gone.GetProperty("removed")[0].GetString() == "w4");
    var unknown = await Call("watch_remove", new() { ["id"] = "w99" });
    Check("an unknown id is not_found", ErrorCode(unknown) == "not_found");
    var neither = await Call("watch_remove");
    Check("remove without id or all is invalid_argument", ErrorCode(neither) == "invalid_argument");

    // The list survives stopping and starting the program.
    await Call("debug_stop");
    await WaitForDesignAsync();
    var restarted = await Call("debug_start");
    Check("after debug_stop and debug_start the list is still there and read again", Watch(restarted, "sum") is not null && Prop(Watch(restarted, "sum")!.Value, "value") == "0");
    await Call("debug_stop");

    // Ends with the agent's hold.
    await Call("vs_release");
    var afterRelease = await Call("watch_list");
    Check("vs_release drops the list", afterRelease.GetProperty("watches").GetArrayLength() == 0);

    // A stop with an empty list adds nothing.
    var plain = await Call("debug_start");
    Check("a stop without a list has no 'watches'", !plain.TryGetProperty("watches", out _));
    await Call("debug_stop");
    await WaitForDesignAsync();
    await Call("bp_remove", new() { ["all"] = true });
}
catch (Exception e)
{
    Check($"the driver threw {e.GetType().Name}: {e.Message}", false);
}
finally
{
    // Leave nothing behind, also after a failure: end the debug session (Visual Studio closes by itself without a question), the server process, then its private copy.
    if (agent is not null)
    {
#pragma warning disable CA1031
        try { await agent.CallToolAsync("debug_stop", new Dictionary<string, object?>()); await agent.CallToolAsync("bp_remove", new Dictionary<string, object?> { ["all"] = true }); await agent.CallToolAsync("vs_release", new Dictionary<string, object?>()); }
        catch (Exception) { /* nothing more to do */ }
#pragma warning restore CA1031
    }

    if (agent is not null) await agent.DisposeAsync();
    try { Directory.Delete(copy, recursive: true); }
    catch (IOException) { Console.WriteLine($"(could not remove {copy})"); }
    catch (UnauthorizedAccessException) { Console.WriteLine($"(could not remove {copy})"); }
}

Console.WriteLine(failures.Count == 0 ? "WATCH: ALL CHECKS PASSED" : $"WATCH: {failures.Count} CHECK(S) FAILED: {string.Join("; ", failures)}");
return failures.Count == 0 ? 0 : 1;
