#:package ModelContextProtocol
#:property PublishAot=false
// Value paging end-to-end driver: eval with offset and length against a running Visual Studio with the Debugging via MCP extension
// (protocol 2.9) and the repository's solution open (OrderDemo = startup project). Run it through tests/e2e/watch.ps1 -Driver value-paging.
// Nothing here answers a consent question: only side-effect-free evaluations (Read = Allow, Control = Allow, the defaults).
//
// Usage: dotnet run tests/e2e/value-paging.cs -- <path-to-DebugViaMcp.Mcp.dll>
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var built = args.Length > 0 ? args[0] : Path.GetFullPath("src/DebugViaMcp.Mcp/bin/Debug/net10.0/DebugViaMcp.Mcp.dll");

// Run a private copy: vs_build may build this very project, and a running server locks its own output files.
var copy = Path.Combine(Path.GetTempPath(), "debugviamcp-e2e-paging-" + Environment.ProcessId);
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

    async Task<JsonElement> Call(string tool, Dictionary<string, object?>? a = null, bool quiet = false)
    {
        var r = await agent.CallToolAsync(tool, a ?? []);
        var text = r.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        if (!quiet)
        {
            Console.WriteLine($"== {tool} {string.Join(", ", (a ?? []).Select(kv => $"{kv.Key}={(kv.Value is string s && s.Length > 90 ? s[..90] + "..." : kv.Value)}"))}{(r.IsError == true ? "  [ERROR]" : "")}");
            Console.WriteLine(text.Length > 300 ? text[..300] + " ..." : text);
            Console.WriteLine();
        }
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    static JsonElement Data(JsonElement answer) => answer.GetProperty("data");
    static string? ErrorCode(JsonElement answer) => answer.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) ? c.GetString() : null;
    async Task WaitForDesignAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            var st = await agent.CallToolAsync("debug_status", new Dictionary<string, object?>());
            if ((st.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "").Contains("\"mode\":\"design\"", StringComparison.Ordinal)) return;
            await Task.Delay(500);
        }
    }

    // Reads every page of an expression and returns the text, how many pages it took and the slowest page.
    async Task<(string Text, int Pages, long SlowestMs, int Total)> ReadAllAsync(string expression, int pageLength)
    {
        var sb = new StringBuilder();
        var pages = 0;
        long slowest = 0;
        var total = -1;
        for (var offset = 0; ; pages++)
        {
            var sw = Stopwatch.StartNew();
            var answer = await Call("eval", new() { ["expression"] = expression, ["offset"] = offset, ["length"] = pageLength }, quiet: true);
            slowest = Math.Max(slowest, sw.ElapsedMilliseconds);
            if (ErrorCode(answer) is { } code) throw new InvalidOperationException($"page at {offset}: {code} {answer}");
            var d = Data(answer);
            total = d.GetProperty("totalLength").GetInt32();
            var value = d.GetProperty("value").GetString()!;
            sb.Append(value);
            if (!d.GetProperty("hasMore").GetBoolean()) return (sb.ToString(), pages + 1, slowest, total);
            offset += value.Length;
        }
    }

    Console.WriteLine("##### value paging");
    var status = await Call("debug_status");
    if (status.GetProperty("permissions").GetProperty("read").GetString() != "allow")
    {
        Check("Read is Allow (the default); this driver answers no question", false);
        return 1;
    }

    await Call("bp_remove", new() { ["all"] = true });
    await Call("bp_set", new() { ["file"] = "samples/OrderDemo/OrderService.cs", ["line"] = 23 });
    var stop = await Call("debug_start");
    Check("debug_start stops at the breakpoint", stop.GetProperty("state").GetString() == "break");

    // A string of 5000 characters (Order.Note): the old read stops at about 1 KB, pages give all of it.
    var whole = await Call("eval", new() { ["expression"] = "order.Note" });
    Check("without offset and length the value is still cut at about 1 KB and says so", Data(whole).GetProperty("truncated").GetBoolean());
    var first = await Call("eval", new() { ["expression"] = "order.Note", ["offset"] = 0, ["length"] = 1000 });
    var fd = Data(first);
    Check("a page of 1000 characters with the total (5000 plus two quotes) and hasMore", fd.GetProperty("value").GetString()!.Length == 1000 && fd.GetProperty("totalLength").GetInt32() == 5002 && fd.GetProperty("hasMore").GetBoolean());
    Check("the first page starts with the quote of the displayed text", fd.GetProperty("value").GetString()![0] == '"');
    var note = await ReadAllAsync("order.Note", 1500);
    Check("all the pages of order.Note put together are the whole displayed text", note.Text == "\"" + new string('x', 5000) + "\"" && note.Pages == 4);

    // A long text made by a side-effect-free expression: 300,000 characters, in pages of at most 16384.
    const string Big = "new string('a', 150000) + new string('b', 150000)";
    var big = await ReadAllAsync(Big, 16384);
    var expected = "\"" + new string('a', 150000) + new string('b', 150000) + "\"";
    Console.WriteLine($"   300,000 characters in {big.Pages} pages, slowest page {big.SlowestMs} ms");
    Check("Visual Studio gives the whole 300,000-character text and the pages rebuild it exactly", big.Total == 300002 && big.Text == expected);
    Check("it took 19 pages of 16384", big.Pages == 19);

    // 1,500,000 characters: the text is read and masked once for all the pages of a stop, so the whole read stays a matter of seconds.
    const string Huge = "new string('a', 750000) + new string('b', 750000)";
    var hugeClock = Stopwatch.StartNew();
    var huge = await ReadAllAsync(Huge, 16384);
    Console.WriteLine($"   1,500,000 characters in {huge.Pages} pages, {hugeClock.ElapsedMilliseconds} ms in all, slowest page {huge.SlowestMs} ms");
    Check("Visual Studio gives 1,500,000 characters and the pages rebuild them exactly", huge.Total == 1500002 && huge.Text == "\"" + new string('a', 750000) + new string('b', 750000) + "\"");
    Check("the whole read took less than 30 seconds", hugeClock.ElapsedMilliseconds < 30_000);

    // Boundaries and refusals.
    var past = await Call("eval", new() { ["expression"] = "order.Note", ["offset"] = 9000, ["length"] = 10 });
    Check("an offset past the end is an empty last page", Data(past).GetProperty("value").GetString() == "" && !Data(past).GetProperty("hasMore").GetBoolean());
    var tooMuch = await Call("eval", new() { ["expression"] = "new string('c', 2100000)", ["offset"] = 0, ["length"] = 100 });
    Console.WriteLine($"   2.5 million characters: {(ErrorCode(tooMuch) ?? "answered")}");
    Check("a text beyond 2,000,000 characters is refused (or Visual Studio's own limit says so), never half read", ErrorCode(tooMuch) is "invalid_argument" or "eval_failed");
    var withEffects = await Call("eval", new() { ["expression"] = "order.Note", ["allowSideEffects"] = true, ["offset"] = 0, ["length"] = 10 });
    Check("paging with allowSideEffects is refused before anything runs", ErrorCode(withEffects) == "invalid_argument");
    var negative = await Call("eval", new() { ["expression"] = "order.Note", ["offset"] = -1, ["length"] = 10 });
    Check("a negative offset is refused", ErrorCode(negative) == "invalid_argument");
    var missing = await Call("eval", new() { ["expression"] = "nope.missing", ["offset"] = 0, ["length"] = 10 });
    Check("an expression that cannot be evaluated is eval_failed with the message", ErrorCode(missing) == "eval_failed");

    await Call("debug_stop");
    await WaitForDesignAsync();
    await Call("bp_remove", new() { ["all"] = true });
    await Call("vs_release");
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

Console.WriteLine(failures.Count == 0 ? "VALUE PAGING: ALL CHECKS PASSED" : $"VALUE PAGING: {failures.Count} CHECK(S) FAILED: {string.Join("; ", failures)}");
return failures.Count == 0 ? 0 : 1;
