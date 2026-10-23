#:package ModelContextProtocol
#:property PublishAot=false
// Exception settings end-to-end driver: talks to the real debugviamcp MCP server over stdio, like Claude Code, against a running
// Visual Studio with the Debugging via MCP extension (protocol 2.8) and the repository's solution open.
// Run it through tests/e2e/exception-settings.ps1, which prepares Visual Studio, the permission settings and the sample for each phase
// and puts everything back. Nothing here answers a consent question.
//
//   allow : Control = Allow. list, misspelled name, set, stop at a caught exception, write while stopped, reset, an unhandled exception,
//           a second agent (in_use while the first holds Visual Studio), and vs_release putting a forgotten change back.
//   deny  : Control = Deny. exception_list still works; set is refused by policy and changes nothing; an empty reset still works.
//   perf  : Control = Allow. Latency of every call and Visual Studio's memory over hundreds of calls and many connect/set/disconnect rounds.
//   ask   : Control = Ask with the shortest timeout. set is asked about, nobody answers, the timeout denies and changes nothing;
//           an empty reset asks nothing.
//
// Usage: dotnet run tests/e2e/exception-settings.cs -- <path-to-DebugViaMcp.Mcp.dll> <allow|deny|ask>
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");
var phase = args.Length > 1 ? args[1] : "allow";

// Run a private copy: vs_build may build this very project, and a running server locks its own output files.
var copy = Path.Combine(Path.GetTempPath(), "debugviamcp-e2e-" + Environment.ProcessId);
Directory.CreateDirectory(copy);
foreach (var f in Directory.GetFiles(Path.GetDirectoryName(built)!)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);

async Task<McpClient> Connect() => await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "debugviamcp",
    Command = "dotnet",
    Arguments = [Path.Combine(copy, Path.GetFileName(built)), "serve"],
}));

var agent = await Connect();
McpClient? second = null;

var failures = new List<string>();
void Check(string what, bool ok) { Console.WriteLine($"   [{(ok ? "PASS" : "FAIL")}] {what}"); if (!ok) failures.Add(what); }

async Task<(string Text, bool IsError)> Call(string tool, Dictionary<string, object?>? arguments = null, McpClient? who = null)
{
    var result = await (who ?? agent).CallToolAsync(tool, arguments ?? []);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
    Console.WriteLine($"== {(who is null ? "A" : "B")}: {tool} {string.Join(", ", (arguments ?? []).Select(kv => $"{kv.Key}={(kv.Value is string[] a ? string.Join("|", a) : kv.Value)}"))}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 900 ? text[..900] + " …" : text);
    Console.WriteLine();
    return (text, result.IsError == true);
}

static string? ErrorCode(string text)
{
    try { return JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) ? c.GetString() : null; }
    catch (JsonException) { return null; }
}

static string? ErrorReason(string text)
{
    try { return JsonDocument.Parse(text).RootElement.GetProperty("error").TryGetProperty("reason", out var r) ? r.GetString() : null; }
    catch (Exception e) when (e is JsonException or KeyNotFoundException) { return null; }
}

const string Clr = "Common Language Runtime Exceptions";
const string Arg = "System.ArgumentException";
const string Timeout = "System.TimeoutException";
string[] argNames = [Arg];
string[] timeoutNames = [Timeout];
string[] misspelled = ["System.ArgumentExeption"];

async Task<bool?> ValueOf(string name, McpClient? who = null)
{
    var (text, _) = await Call("exception_list", new() { ["group"] = Clr, ["filter"] = name, ["onlyEnabled"] = false }, who);
    if (ErrorCode(text) is not null) return null;
    foreach (var s in JsonDocument.Parse(text).RootElement.GetProperty("groups")[0].GetProperty("settings").EnumerateArray())
        if (s.GetProperty("name").GetString() == name) return s.GetProperty("breakWhenThrown").GetBoolean();
    return null;
}

Task<(string Text, bool IsError)> Set(string[] names, bool value, McpClient? who = null) =>
    Call("exception_set", new() { ["group"] = Clr, ["names"] = names, ["breakWhenThrown"] = value }, who);

Console.WriteLine($"##### phase: {phase}");
var (instances, _) = await Call("vs_instances");
Check("a Visual Studio instance is found", instances.Contains("\"pid\"", StringComparison.Ordinal));
var before = await ValueOf(Arg);
var beforeTimeout = await ValueOf(Timeout);
Check($"the exceptions are listed (ArgumentException {before}, TimeoutException {beforeTimeout})", before is not null && beforeTimeout is not null);

try
{
    switch (phase)
    {
        case "allow":
            await Allow();
            break;
        case "deny":
            await Deny();
            break;
        case "ask":
            await Ask();
            break;
        case "perf":
            await Perf();
            break;
        default:
            Check($"unknown phase '{phase}'", false);
            break;
    }
}
finally
{
    // Leave nothing behind, also after a failure: the two server processes, then their private copy of the server (23 MB over four runs once piled up in %TEMP%).
    if (second is not null) await second.DisposeAsync();
    await agent.DisposeAsync();
    try { Directory.Delete(copy, recursive: true); }
    catch (IOException) { Console.WriteLine($"(could not remove {copy})"); }
    catch (UnauthorizedAccessException) { Console.WriteLine($"(could not remove {copy})"); }
}

Console.WriteLine(failures.Count == 0 ? $"PHASE {phase}: ALL CHECKS PASSED" : $"PHASE {phase}: {failures.Count} CHECK(S) FAILED: {string.Join("; ", failures)}");
return failures.Count == 0 ? 0 : 1;

async Task Allow()
{
    var (listed, listError) = await Call("exception_list");
    Check("exception_list answers with no arguments", !listError && listed.Contains("\"groups\"", StringComparison.Ordinal));

    var (bogus, bogusError) = await Set(misspelled, true);
    Check("a misspelled name is not_found and offers System.ArgumentException", bogusError && ErrorCode(bogus) == "not_found" && bogus.Contains(Arg, StringComparison.Ordinal));
    Check("the misspelled call changed nothing", await ValueOf(Arg) == before);

    var (set, setError) = await Set(argNames, true);
    Check("exception_set turns Break When Thrown on", !setError && await ValueOf(Arg) == true);
    Check("the answer points to exception_reset", before == true || set.Contains("exception_reset", StringComparison.Ordinal));

    // A second agent from the same folder: refused, with nothing changed, while the first one holds Visual Studio.
    second = await Connect();
    var (blockedList, _) = await Call("exception_list", new() { ["group"] = Clr, ["filter"] = Timeout }, second);
    Check("a second agent's read is refused with in_use", ErrorCode(blockedList) == "in_use");
    var (blockedSet, _) = await Set(timeoutNames, true, second);
    Check("a second agent's set is refused with in_use", ErrorCode(blockedSet) == "in_use");
    Check("the first agent still reads its own change", await ValueOf(Arg) == true);

    var (build, _) = await Call("vs_build", new() { ["project"] = "FaultDemo" });
    Check("FaultDemo builds", build.Contains("\"succeeded\":true", StringComparison.Ordinal));

    var (start, _) = await Call("debug_start", new() { ["project"] = "FaultDemo" });
    Check("the program stops at the CAUGHT ArgumentException (it would not without Break When Thrown)", start.Contains("\"reason\":\"exception\"", StringComparison.Ordinal));
    var (ex, _) = await Call("exception");
    Check("the current exception is the ArgumentException of Ticker.Fail", ex.Contains("ArgumentException", StringComparison.Ordinal) && ex.Contains("out of range", StringComparison.Ordinal));

    // While the program is stopped: write another setting and read it back.
    var (_, stoppedError) = await Set(timeoutNames, beforeTimeout != true);
    Check("a set while stopped at a break works", !stoppedError && await ValueOf(Timeout) == (beforeTimeout != true));

    var (reset, resetError) = await Call("exception_reset");
    Check("exception_reset reports both changes", !resetError && reset.Contains(Timeout, StringComparison.Ordinal));
    Check("both settings are back at their first values", await ValueOf(Arg) == before && await ValueOf(Timeout) == beforeTimeout);

    var (emptyReset, emptyError) = await Call("exception_reset");
    Check("a second reset has nothing to restore and says so", !emptyError && emptyReset.Contains("\"restored\":[]", StringComparison.Ordinal));

    var (cont, _) = await Call("debug_continue");
    Check("the unhandled InvalidOperationException still stops the program", cont.Contains("\"reason\":\"exception\"", StringComparison.Ordinal));
    var (ex2, _) = await Call("exception");
    Check("it carries the ArgumentException as its inner exception", ex2.Contains("InvalidOperationException", StringComparison.Ordinal) && ex2.Contains("ArgumentException", StringComparison.Ordinal));
    await Call("debug_stop");

    // Forgetting to reset: letting go of Visual Studio puts the change back, and then the second agent is let in.
    await Set(argNames, before != true);
    Check("a change is in place before letting go", await ValueOf(Arg) == (before != true));
    await Call("vs_release");
    Check("vs_release put it back without exception_reset", await ValueOf(Arg) == before);
    var (freeList, _) = await Call("exception_list", new() { ["group"] = Clr, ["filter"] = Timeout }, second);
    Check("the second agent can read once the first let go", ErrorCode(freeList) is null);
}

async Task Deny()
{
    var (listed, listError) = await Call("exception_list", new() { ["group"] = Clr, ["filter"] = Arg });
    Check("a read still works with Control = Deny", !listError && listed.Contains("\"groups\"", StringComparison.Ordinal));

    var (set, setError) = await Set(argNames, before != true);
    Check("exception_set is denied by policy", setError && ErrorCode(set) == "denied" && ErrorReason(set) == "policy");
    Check("a denied set changed nothing", await ValueOf(Arg) == before);

    var (reset, resetError) = await Call("exception_reset");
    Check("exception_reset with nothing recorded is a harmless read", !resetError && reset.Contains("\"restored\":[]", StringComparison.Ordinal));
}

async Task Ask()
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var (set, setError) = await Set(argNames, before != true);
    watch.Stop();
    Check("exception_set was asked about and the unanswered question denied it", setError && ErrorCode(set) == "denied" && ErrorReason(set) == "timeout");
    Check($"the answer came only after the question timed out ({watch.Elapsed.TotalSeconds:0} s, shortest allowed is 15)", watch.Elapsed.TotalSeconds >= 10);
    Check("an unanswered set changed nothing", await ValueOf(Arg) == before);

    watch.Restart();
    var (reset, resetError) = await Call("exception_reset");
    watch.Stop();
    Check("exception_reset with nothing recorded asks nothing", !resetError && reset.Contains("\"restored\":[]", StringComparison.Ordinal) && watch.Elapsed.TotalSeconds < 8);
}

async Task Perf()
{
    const int WarmUp = 20, CyclesPerRound = 100, Reconnects = 30;
    var Rounds = int.TryParse(Environment.GetEnvironmentVariable("E2E_PERF_ROUNDS"), out var r) && r >= 3 ? r : 12;   // Visual Studio keeps garbage for a while: it rose to 1480 MB and settled near 1370 MB over 20 rounds, so a short run misreads it
    const double P95BudgetMs = 400, ClimbBudgetMb = 32;

    var vsPid = int.Parse(System.Text.RegularExpressions.Regex.Match(instances, @"""pid""\s*:\s*(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    using var vs = Process.GetProcessById(vsPid);
    double PrivateMb() { vs.Refresh(); return vs.PrivateMemorySize64 / 1048576.0; }

    var times = new Dictionary<string, List<double>> { ["debug_status (baseline)"] = [], ["exception_list"] = [], ["exception_list (filter)"] = [], ["exception_set"] = [], ["exception_reset"] = [] };
    async Task<string> Timed(string label, string tool, Dictionary<string, object?> arguments, McpClient? who = null)
    {
        var watch = Stopwatch.StartNew();
        var result = await (who ?? agent).CallToolAsync(tool, arguments);
        watch.Stop();
        times[label].Add(watch.Elapsed.TotalMilliseconds);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        if (result.IsError == true) throw new InvalidOperationException($"{tool} failed during the run: {text}");
        return text;
    }

    async Task Cycle(int i)
    {
        await Timed("debug_status (baseline)", "debug_status", []);   // the same trip (MCP, pipe, UI thread) without touching Exception Settings
        await Timed("exception_list", "exception_list", []);
        await Timed("exception_list (filter)", "exception_list", new() { ["group"] = Clr, ["filter"] = "Argument", ["onlyEnabled"] = false });
        await Timed("exception_set", "exception_set", new() { ["group"] = Clr, ["names"] = argNames, ["breakWhenThrown"] = (i % 2 == 0) != (before == true) });
        await Timed("exception_reset", "exception_reset", []);
    }

    for (var i = 0; i < WarmUp; i++) await Cycle(i);
    foreach (var list in times.Values) list.Clear();

    // A solution that is still loading keeps allocating: take the baseline once the memory has settled (two samples 5 s apart within 10 MB).
    var last = PrivateMb();
    for (var i = 0; i < 18; i++)
    {
        await Task.Delay(5000);
        var now = PrivateMb();
        if (Math.Abs(now - last) < 10) break;
        last = now;
    }

    var baseline = PrivateMb();
    var samples = new List<double> { baseline };
    Console.WriteLine($"   Visual Studio {vsPid} private memory after warm-up: {baseline:0.0} MB");

    for (var round = 1; round <= Rounds; round++)
    {
        for (var i = 0; i < CyclesPerRound; i++) await Cycle(i);
        samples.Add(PrivateMb());
        Console.WriteLine($"   after {round * CyclesPerRound} cycles: {samples[^1]:0.0} MB");
    }

    foreach (var (label, list) in times)
    {
        var sorted = list.Order().ToArray();
        var p50 = sorted[sorted.Length / 2];
        var p95 = sorted[(int)(sorted.Length * 0.95)];
        Console.WriteLine($"   {label,-26} n={sorted.Length,4}  p50 {p50,6:0.0} ms  p95 {p95,6:0.0} ms  max {sorted[^1],6:0.0} ms");
        Check($"{label}: p95 {p95:0} ms is within {P95BudgetMs:0} ms", p95 <= P95BudgetMs);
    }

    // Visual Studio keeps garbage until it is under pressure and a loading solution allocates too, so the end-minus-start number means little. A leak
    // keeps climbing round after round; garbage does not. Look at the last three rounds.
    var climbing = samples[^1] > samples[^2] && samples[^2] > samples[^3] && samples[^1] - samples[^3] > ClimbBudgetMb;
    Check($"memory is not climbing round after round (last rounds {samples[^3]:0} -> {samples[^2]:0} -> {samples[^1]:0} MB; a rise of more than {ClimbBudgetMb:0} MB over two rounds in a row fails)", !climbing);
    Check("every cycle ended with the setting at its first value", await ValueOf(Arg) == before);

    // Many short-lived agents: each takes Visual Studio, changes a setting and vanishes without a reset.
    await Call("vs_release");   // the main agent holds Visual Studio since its first set; the visitors need it free
    var beforeChurn = PrivateMb();
    var stuck = 0;
    for (var i = 0; i < Reconnects; i++)
    {
        await using var visitor = await Connect();
        var done = false;
        for (var attempt = 0; attempt < 25 && !done; attempt++)   // the previous visitor's hold may still be ending
        {
            var result = await visitor.CallToolAsync("exception_set", new Dictionary<string, object?> { ["group"] = Clr, ["names"] = argNames, ["breakWhenThrown"] = before != true });
            done = result.IsError != true;
            if (!done) await Task.Delay(200);
        }

        if (!done) stuck++;
    }

    await Task.Delay(1500);
    Check($"{Reconnects} agents that came, changed a setting and left", stuck == 0);
    Check("every one of them left the setting as the user had it", await ValueOf(Arg) == before);
    var afterChurn = PrivateMb();
    Check($"memory after {Reconnects} connections changed by {afterChurn - beforeChurn:+0.0;-0.0} MB (fails above +{ClimbBudgetMb:0} MB)", afterChurn - beforeChurn <= ClimbBudgetMb);
    Console.WriteLine($"   Visual Studio private memory: start {baseline:0.0} MB, end {afterChurn:0.0} MB");
}
