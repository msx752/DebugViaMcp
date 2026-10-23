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
//   ask   : Control = Ask with the shortest timeout. set is asked about, nobody answers, the timeout denies and changes nothing;
//           an empty reset asks nothing.
//
// Usage: dotnet run tests/e2e/exception-settings.cs -- <path-to-DebugViaMcp.Mcp.dll> <allow|deny|ask>
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
