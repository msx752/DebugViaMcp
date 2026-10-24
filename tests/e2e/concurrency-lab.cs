#:package ModelContextProtocol
#:property PublishAot=false
// Concurrency lab end-to-end driver (docs/design/concurrency-lab-design.md): the driver acts like an agent that has to find three
// reproducible concurrency bugs of samples/ConcurrencyLab with the MCP tools alone, and checks the diagnosis, not the steps.
// Run it through tests/e2e/watch.ps1 -Driver concurrency-lab. Nothing here answers a consent question (Read, Control = Allow),
// and nothing is written outside the sample's build output (scenario.txt, removed at the end).
//
//   race      both workers have passed the check "Status == Pending" before either charged; afterwards the order was charged twice
//   deadlock  two threads wait inside Transfer; Visual Studio's own stack lines say each waits for a lock the other one owns (a cycle)
//   crash     the default stop is at the wrapper in Handle (the exception carries the cause as inner); with Break When Thrown on, the stop is in Validate (order 7)
//
// Usage: dotnet run tests/e2e/concurrency-lab.cs -- <path-to-DebugViaMcp.Mcp.dll>
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");
const string Project = "ConcurrencyLab";
const string Clr = "Common Language Runtime Exceptions";
var sampleFile = Path.GetFullPath("samples/ConcurrencyLab/Scenarios.cs");
var scenarioFile = Path.GetFullPath("samples/ConcurrencyLab/bin/Debug/net10.0/scenario.txt");

// Run a private copy: vs_build may build this very project, and a running server locks its own output files.
var copy = Path.Combine(Path.GetTempPath(), "debugviamcp-e2e-lab-" + Environment.ProcessId);
Directory.CreateDirectory(copy);
foreach (var f in Directory.GetFiles(Path.GetDirectoryName(built)!)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);

var failures = new List<string>();
void Check(string what, bool ok)
{
    Console.WriteLine($"   [{(ok ? "PASS" : "FAIL")}] {what}");
    if (!ok) failures.Add(what);
}

// The line of the source that holds this text, so the driver follows the sample when it is edited.
int LineOf(string text) => Array.FindIndex(File.ReadAllLines(sampleFile), l => l.Contains(text, StringComparison.Ordinal)) + 1;

McpClient? agent = null;
var calls = 0;
try
{
    agent = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "debugviamcp", Command = "dotnet", Arguments = [Path.Combine(copy, Path.GetFileName(built)), "serve"],
    }));

    async Task<JsonElement> Call(string tool, Dictionary<string, object?>? a = null)
    {
        calls++;
        var r = await agent.CallToolAsync(tool, a ?? []);
        var text = r.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        Console.WriteLine($"== {tool} {string.Join(", ", (a ?? []).Select(kv => $"{kv.Key}={kv.Value}"))}{(r.IsError == true ? "  [ERROR]" : "")}");
        Console.WriteLine(text.Length > 380 ? text[..380] + " ..." : text);
        Console.WriteLine();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? (p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString()) : null;
    static JsonElement Snapshot(JsonElement answer) => answer.GetProperty("snapshot");
    static string Function(JsonElement answer) => Snapshot(answer).GetProperty("location").GetProperty("function").GetString() ?? "";
    static int ThreadId(JsonElement answer) => Snapshot(answer).GetProperty("threadId").GetInt32();
    static string? Watched(JsonElement answer, string expression) =>
        answer.TryGetProperty("watches", out var list) ? list.EnumerateArray().Where(w => Str(w, "expression") == expression).Select(w => Str(w, "value")).FirstOrDefault() : null;
    async Task WaitForDesignAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            var st = await agent.CallToolAsync("debug_status", new Dictionary<string, object?>());
            if ((st.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "").Contains("\"mode\":\"design\"", StringComparison.Ordinal)) return;
            await Task.Delay(500);
        }
    }
    async Task<JsonElement> StartAsync(string scenario)
    {
        File.WriteAllText(scenarioFile, scenario);
        return await Call("debug_start", new() { ["project"] = Project });
    }
    async Task EndAsync()
    {
        await Call("debug_stop");
        await WaitForDesignAsync();
        await Call("bp_remove", new() { ["all"] = true });
        await Call("watch_remove", new() { ["all"] = true });
    }

    Console.WriteLine("##### concurrency lab");
    var status = await Call("debug_status");
    if (Str(status.GetProperty("permissions"), "read") != "allow" || Str(status.GetProperty("permissions"), "control") != "allow")
    {
        Check("Read and Control are Allow (the defaults); this driver answers no question", false);
        return 1;
    }

    await Call("bp_remove", new() { ["all"] = true });
    await Call("watch_remove", new() { ["all"] = true });
    var build = await Call("vs_build");
    Check("the solution builds, the sample included", Str(build, "succeeded") == "True");
    if (Str(build, "succeeded") != "True") await Call("errors_list", new() { ["severity"] = "error" });
    Directory.CreateDirectory(Path.GetDirectoryName(scenarioFile)!);

    // ---- race: both workers pass the check before either one charges ----
    Console.WriteLine("##### race");
    calls = 0;
    await Call("bp_set", new() { ["file"] = "samples/ConcurrencyLab/Scenarios.cs", ["line"] = LineOf("both workers have checked") });
    await Call("bp_set", new() { ["file"] = "samples/ConcurrencyLab/Scenarios.cs", ["line"] = LineOf("time to look at it") });
    await Call("watch_add", new() { ["expression"] = "order.Status" });
    await Call("watch_add", new() { ["expression"] = "order.ChargeCount" });
    var first = await StartAsync("race");
    Check("the first worker stops at the barrier", Str(first, "state") == "break" && Function(first).Contains("Claim", StringComparison.Ordinal));
    Check("it has passed the check: the order is still Pending and uncharged", Watched(first, "order.Status") == "\"Pending\"" && Watched(first, "order.ChargeCount") == "0");
    var second = await Call("debug_continue");
    Check("the second worker stops at the same line, on another thread", Str(second, "state") == "break" && Function(second).Contains("Claim", StringComparison.Ordinal) && ThreadId(second) != ThreadId(first));
    Check("it too sees the order Pending and uncharged: both passed the check before either charged", Watched(second, "order.Status") == "\"Pending\"" && Watched(second, "order.ChargeCount") == "0");
    var end = await Call("debug_continue");
    Check("after both workers finished the order was charged twice", Str(end, "state") == "break" && Function(end).Contains("Race", StringComparison.Ordinal) && Watched(end, "order.ChargeCount") == "2");
    Console.WriteLine($"   race diagnosed in {calls} calls");
    await EndAsync();

    // ---- deadlock: two threads, each holding the lock the other one wants ----
    Console.WriteLine("##### deadlock");
    calls = 0;
    var running = await StartAsync("deadlock");
    Check("the program does not stop by itself", Str(running, "state") == "running");
    var paused = await Call("debug_pause");
    Check("debug_pause stops it", Str(paused, "state") == "break" || Str(paused, "mode") == "break");
    var threads = await Call("threads");
    // Visual Studio names what a blocked thread waits for in its own stack lines: "[Waiting on lock owned by Thread 0xE48, ...]".
    var waits = new Dictionary<int, int>();
    var insideTransfer = new List<int>();
    var lastTransferThread = 0;
    JsonElement lastStack = default;
    foreach (var t in threads.GetProperty("threads").EnumerateArray())
    {
        var id = t.GetProperty("id").GetInt32();
        var stack = await Call("stack", new() { ["threadId"] = id });
        var functions = stack.GetProperty("frames").EnumerateArray().Select(f => Str(f, "function") ?? "").ToList();
        if (functions.Any(f => f.Contains("Transfer", StringComparison.Ordinal))) { insideTransfer.Add(id); lastTransferThread = id; lastStack = stack; }
        var waiting = functions.FirstOrDefault(f => f.Contains("Waiting on lock owned by Thread 0x", StringComparison.Ordinal));
        if (waiting is not null)
        {
            var hex = waiting[(waiting.IndexOf("Thread 0x", StringComparison.Ordinal) + "Thread 0x".Length)..].TakeWhile(Uri.IsHexDigit).ToArray();
            waits[id] = Convert.ToInt32(new string(hex), 16);
        }
    }

    Console.WriteLine($"   waits: {string.Join("; ", waits.Select(w => $"{w.Key} waits for the lock of {w.Value}"))}");
    Check("two threads are inside Transfer", insideTransfer.Count == 2);
    Check("Visual Studio says both wait for a lock", waits.Count == 2 && insideTransfer.All(waits.ContainsKey));
    Check("each one waits for the lock the other one owns: a cycle, a deadlock", waits.Count == 2 && waits.All(w => waits.TryGetValue(w.Value, out var back) && back == w.Key));

    // Inside the blocked frame Visual Studio cannot read variables or run code: the answer says why (never an internal error).
    var transferIndex = lastStack.GetProperty("frames").EnumerateArray().First(f => (Str(f, "function") ?? "").Contains("Transfer", StringComparison.Ordinal)).GetProperty("index").GetInt32();
    await Call("frame_select", new() { ["index"] = transferIndex, ["epoch"] = lastStack.GetProperty("epoch").GetInt64(), ["threadId"] = lastTransferThread });
    var blockedVars = await Call("vars", new() { ["scope"] = "args" });
    Check("variables of a blocked frame: values, or a reason that points to stack; never an internal error",
        !blockedVars.TryGetProperty("error", out var varsError) || (Str(varsError, "code") == "eval_failed" && (Str(varsError, "message") ?? "").Contains("stack", StringComparison.Ordinal)));
    var blockedEval = await Call("eval", new() { ["expression"] = "first.Name" });
    Check("an expression on a blocked thread: a value, or a reason; never an internal error", !blockedEval.TryGetProperty("error", out var evalError) || Str(evalError, "code") == "eval_failed");
    Console.WriteLine($"   deadlock diagnosed in {calls} calls");
    await EndAsync();

    // ---- crash: where the exception shows up and where it started ----
    Console.WriteLine("##### crash");
    calls = 0;
    var unhandled = await StartAsync("crash");
    Check("by default Visual Studio stops at the wrapper (user-unhandled), in Handle, not in Validate where it began", Str(unhandled, "state") == "break" && Function(unhandled).Contains("Handle", StringComparison.Ordinal));
    var shown = await Call("exception");
    var shownData = shown.GetProperty("data");
    Check("the exception that arrived is the wrapper (OrderFailedException)", Str(shownData, "type") is { } type && type.Contains("OrderFailedException", StringComparison.Ordinal));
    Check("its inner exceptions lead to the cause: stock below zero", shownData.TryGetProperty("inner", out var inner) && inner.ToString().Contains("stock below zero", StringComparison.Ordinal));
    await Call("debug_stop");
    await WaitForDesignAsync();

    string[] thrownNames = ["System.InvalidOperationException"];
    var set = await Call("exception_set", new() { ["group"] = Clr, ["names"] = thrownNames, ["breakWhenThrown"] = true });
    Check("Break When Thrown is turned on for InvalidOperationException", set.TryGetProperty("changes", out _));
    var thrown = await StartAsync("crash");
    Check("now the stop is where it is thrown: in Validate", Str(thrown, "state") == "break" && Function(thrown).Contains("Validate", StringComparison.Ordinal));
    var order = await Call("eval", new() { ["expression"] = "order.Id" });
    Check("the order that failed is 7", order.GetProperty("data").GetProperty("value").GetString() == "7");
    Console.WriteLine($"   crash diagnosed in {calls} calls");
    await Call("exception_reset");
    var after = await Call("exception_list", new() { ["group"] = Clr, ["filter"] = "System.InvalidOperationException", ["onlyEnabled"] = false });
    Check("exception_reset puts the setting back", after.GetProperty("groups")[0].GetProperty("settings").EnumerateArray()
        .Where(s => Str(s, "name") == "System.InvalidOperationException").All(s => Str(s, "breakWhenThrown") == "False"));
    await EndAsync();
    await Call("vs_release");
}
catch (Exception e)
{
    Check($"the driver threw {e.GetType().Name}: {e.Message}", false);
}
finally
{
    // Leave nothing behind, also after a failure: end the debug session (Visual Studio closes by itself without a question), put back
    // what this agent changed, the server process, then its private copy and the scenario file.
    if (agent is not null)
    {
#pragma warning disable CA1031
        try
        {
            await agent.CallToolAsync("debug_stop", new Dictionary<string, object?>());
            await agent.CallToolAsync("exception_reset", new Dictionary<string, object?>());
            await agent.CallToolAsync("bp_remove", new Dictionary<string, object?> { ["all"] = true });
            await agent.CallToolAsync("vs_release", new Dictionary<string, object?>());
        }
        catch (Exception) { /* nothing more to do */ }
#pragma warning restore CA1031
        await agent.DisposeAsync();
    }

    try { File.Delete(scenarioFile); }
    catch (IOException) { Console.WriteLine($"(could not remove {scenarioFile})"); }
    try { Directory.Delete(copy, recursive: true); }
    catch (IOException) { Console.WriteLine($"(could not remove {copy})"); }
    catch (UnauthorizedAccessException) { Console.WriteLine($"(could not remove {copy})"); }
}

Console.WriteLine(failures.Count == 0 ? "CONCURRENCY LAB: ALL CHECKS PASSED" : $"CONCURRENCY LAB: {failures.Count} CHECK(S) FAILED: {string.Join("; ", failures)}");
return failures.Count == 0 ? 0 : 1;
