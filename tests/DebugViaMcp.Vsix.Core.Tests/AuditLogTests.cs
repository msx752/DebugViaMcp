using Newtonsoft.Json.Linq;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

public sealed class AuditLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("debugviamcp-audit-").FullName;
    private readonly List<string> _activity = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private sealed class ListSink(List<string> lines) : IActivitySink
    {
        public void Write(string line)
        {
            lock (lines) lines.Add(line);
        }
    }

    private AuditLog Create(long maxFileBytes = AuditLog.MaxFileBytes) =>
        new(_dir, () => 7, new ListSink(_activity), new NullLog(), () => Now, maxFileBytes);

    private static AuditEntry SetValueEntry() => new()
    {
        Ts = Now, CallId = "c-000042", Client = "claude-code", Operation = "set_value", Tier = "mutate",
        Params = new() { ["expression"] = "total", ["value"] = "0" },
        Decision = AuditDecision.AllowOnce, Outcome = "ok", DurationMs = 12, Summary = "total = 0",
    };

    [Fact]
    public void Writes_one_json_line_per_entry_with_call_id()
    {
        Create().Record(SetValueEntry());

        var line = Assert.Single(File.ReadAllLines(Path.Combine(_dir, "audit-20261011.log")));
        var json = JObject.Parse(line);
        Assert.Equal("c-000042", (string?)json["callId"]);
        Assert.Equal("set_value", (string?)json["operation"]);
        Assert.Equal("mutate", (string?)json["tier"]);
        Assert.Equal("0", (string?)json["params"]!["value"]);
        Assert.Equal("allowOnce", (string?)json["decision"]);
        Assert.Equal("ok", (string?)json["outcome"]);
        Assert.Equal(12, (long)json["durationMs"]!);
        Assert.Null(json["summary"]);
    }

    [Fact]
    public void Each_entry_also_goes_to_the_activity_pane()
    {
        Create().Record(SetValueEntry());

        var expected = $"{Now.ToLocalTime():HH:mm:ss}  c-000042  set_value  total = 0  → allowed (once)  ok  12 ms";
        Assert.Equal(expected, Assert.Single(_activity));
    }

    [Fact]
    public void Events_without_a_decision_have_a_short_activity_line()
    {
        Create().Record(new AuditEntry { Ts = Now, Client = "claude-code", Operation = "connect", Outcome = "ok", Summary = "claude-code" });

        Assert.Equal($"{Now.ToLocalTime():HH:mm:ss}  connect  claude-code", Assert.Single(_activity));
    }

    [Fact]
    public void Rolls_to_a_numbered_file_when_full()
    {
        var log = Create(maxFileBytes: 400);
        log.Record(SetValueEntry());
        log.Record(SetValueEntry());

        Assert.True(File.Exists(Path.Combine(_dir, "audit-20261011.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "audit-20261011-1.log")));
    }

    [Fact]
    public void Prunes_files_older_than_retention()
    {
        var old = Path.Combine(_dir, "audit-20261001.log");
        var recent = Path.Combine(_dir, "audit-20261009.log");
        var other = Path.Combine(_dir, "mcp-20261001.log");
        foreach (var f in new[] { old, recent, other }) File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(old, Now.UtcDateTime.AddDays(-10));
        File.SetLastWriteTimeUtc(recent, Now.UtcDateTime.AddDays(-2));
        File.SetLastWriteTimeUtc(other, Now.UtcDateTime.AddDays(-10));

        Create().Record(SetValueEntry());

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void An_unwritable_folder_never_throws()
    {
        var file = Path.Combine(_dir, "not-a-dir");
        File.WriteAllText(file, "x");
        var log = new AuditLog(file, () => 7, new ListSink(_activity), new NullLog(), () => Now);

        log.Record(SetValueEntry());

        Assert.Single(_activity);
    }

    /// <summary>Security review: agent-controlled text must not be able to forge extra Activity lines.</summary>
    [Fact]
    public void Activity_lines_escape_agent_controlled_fields()
    {
        var nl = ((char)10).ToString();
        var entry = SetValueEntry();
        entry.CallId = "c-1" + nl + "12:00:00  c-2  set_value  x = 1  → allowed (once)  ok  1 ms";
        entry.Summary = "total = 0" + nl + "fake line";
        entry.Outcome = "ok" + nl + "x";

        Create().Record(entry);

        var line = Assert.Single(_activity);
        Assert.DoesNotContain(nl, line, StringComparison.Ordinal);
        Assert.DoesNotContain((char)13, line);
    }

    private sealed class ThrowingSink : IActivitySink
    {
        public void Write(string line) => throw new InvalidOperationException("pane closed");
    }

    /// <summary>M2 review minor 5.</summary>
    [Fact]
    public void A_failing_activity_pane_never_stops_the_audit_file()
    {
        new AuditLog(_dir, () => 7, new ThrowingSink(), new NullLog(), () => Now).Record(SetValueEntry());

        Assert.Single(Directory.GetFiles(_dir, "audit-*.log").SelectMany(File.ReadAllLines));
    }
}
