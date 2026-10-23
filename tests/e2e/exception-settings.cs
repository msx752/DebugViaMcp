#:package ModelContextProtocol
#:property PublishAot=false
// F03 end-to-end driver: talks to the real debugviamcp MCP server over stdio, like Claude Code, against a running
// Visual Studio with the Debugging via MCP extension (protocol 2.8) and the repository's solution open.
// FaultDemo must start with "crash 15": create samples/FaultDemo/Properties/launchSettings.json with
// {"profiles":{"FaultDemo":{"commandName":"Project","commandLineArgs":"crash 15"}}} for the run and delete it afterwards.
// Nothing here answers a consent question: the Control tools must be on Allow (the default) in Options.
// Usage: dotnet run tests/e2e/exception-settings.cs -- <path-to-DebugViaMcp.Mcp.dll>
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");

// Run a private copy: vs_build may build this very project, and a running server locks its own output files.
var copy = Path.Combine(Path.GetTempPath(), "debugviamcp-e2e-" + Environment.ProcessId);
Directory.CreateDirectory(copy);
foreach (var f in Directory.GetFiles(Path.GetDirectoryName(built)!)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);
await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "debugviamcp",
    Command = "dotnet",
    Arguments = [Path.Combine(copy, Path.GetFileName(built)), "serve"],
}));

var failures = new List<string>();
void Check(string what, bool ok) { Console.WriteLine($"   [{(ok ? "PASS" : "FAIL")}] {what}"); if (!ok) failures.Add(what); }

async Task<(string Text, bool IsError)> Call(string tool, Dictionary<string, object?>? arguments = null)
{
    var result = await client.CallToolAsync(tool, arguments ?? []);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
    Console.WriteLine($"== {tool} {string.Join(", ", (arguments ?? []).Select(kv => $"{kv.Key}={(kv.Value is string[] a ? string.Join("|", a) : kv.Value)}"))}{(result.IsError == true ? "  [ERROR]" : "")}");
    Console.WriteLine(text.Length > 1800 ? text[..1800] + " …" : text);
    Console.WriteLine();
    return (text, result.IsError == true);
}

const string Clr = "Common Language Runtime Exceptions";
const string Arg = "System.ArgumentException";
string[] argNames = [Arg];
string[] misspelled = ["System.ArgumentExeption"];

async Task<bool?> ArgValue()
{
    var (text, _) = await Call("exception_list", new() { ["group"] = Clr, ["filter"] = Arg, ["onlyEnabled"] = false });
    foreach (var s in JsonDocument.Parse(text).RootElement.GetProperty("groups")[0].GetProperty("settings").EnumerateArray())
        if (s.GetProperty("name").GetString() == Arg) return s.GetProperty("breakWhenThrown").GetBoolean();
    return null;
}

// 1. Connection and a plain read
var (instances, _) = await Call("vs_instances");
Check("a Visual Studio instance is found", instances.Contains("\"pid\"", StringComparison.Ordinal));
var (status, _) = await Call("debug_status");
Check("debugger is in design mode", status.Contains("design", StringComparison.OrdinalIgnoreCase));

var (listed, listError) = await Call("exception_list");
Check("exception_list answers (protocol 2.8 on both sides)", !listError && listed.Contains("\"groups\"", StringComparison.Ordinal));

var before = await ArgValue();
Check($"System.ArgumentException is listed (Break When Thrown is {before})", before is not null);

// 2. An unknown name changes nothing and offers the right one
var (bogus, bogusError) = await Call("exception_set", new() { ["group"] = Clr, ["names"] = misspelled, ["breakWhenThrown"] = true });
Check("a misspelled name is an error that offers System.ArgumentException", bogusError && bogus.Contains(Arg, StringComparison.Ordinal));
Check("the misspelled call changed nothing", await ArgValue() == before);

// 3. Break When Thrown on a first-chance exception that the program catches
var (set, setError) = await Call("exception_set", new() { ["group"] = Clr, ["names"] = argNames, ["breakWhenThrown"] = true });
Check("exception_set turns it on", !setError && await ArgValue() == true);
Check("the answer points to exception_reset", before == true || set.Contains("exception_reset", StringComparison.Ordinal));

var (build, _) = await Call("vs_build", new() { ["project"] = "FaultDemo" });
Check("FaultDemo builds", build.Contains("\"succeeded\":true", StringComparison.Ordinal));

var (start, _) = await Call("debug_start", new() { ["project"] = "FaultDemo" });
Check("the program stops at the CAUGHT ArgumentException (it would not without Break When Thrown)",
    start.Contains("\"reason\":\"exception\"", StringComparison.Ordinal));
var (ex, _) = await Call("exception");
Check("the current exception is the ArgumentException of Ticker.Fail",
    ex.Contains("ArgumentException", StringComparison.Ordinal) && ex.Contains("out of range", StringComparison.Ordinal));

// 4. Reset puts back what was changed, then the unhandled one still stops
var (reset, resetError) = await Call("exception_reset");
Check("exception_reset reports what it put back", !resetError && (before == true || reset.Contains(Arg, StringComparison.Ordinal)));
Check("Break When Thrown is back at its first value", await ArgValue() == before);

var (cont, _) = await Call("debug_continue");
Check("the unhandled InvalidOperationException still stops the program", cont.Contains("\"reason\":\"exception\"", StringComparison.Ordinal));
var (ex2, _) = await Call("exception");
Check("it carries the ArgumentException as its inner exception",
    ex2.Contains("InvalidOperationException", StringComparison.Ordinal) && ex2.Contains("ArgumentException", StringComparison.Ordinal));
await Call("debug_stop");

// 5. Letting go of Visual Studio puts a forgotten change back by itself
await Call("exception_set", new() { ["group"] = Clr, ["names"] = argNames, ["breakWhenThrown"] = before != true });
Check("a second change is in place", await ArgValue() == (before != true));
await Call("vs_release");
Check("vs_release put it back without exception_reset", await ArgValue() == before);

Console.WriteLine(failures.Count == 0 ? "ALL CHECKS PASSED" : $"{failures.Count} CHECK(S) FAILED: {string.Join("; ", failures)}");
return failures.Count == 0 ? 0 : 1;
