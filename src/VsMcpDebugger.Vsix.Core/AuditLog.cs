using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>One audited event. Params hold what the agent sent; values read from the debuggee never go here.</summary>
public sealed class AuditEntry
{
    public DateTimeOffset Ts { get; set; }
    public string CallId { get; set; } = "";
    public string Client { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Tier { get; set; } = "";
    public Dictionary<string, string?> Params { get; set; } = [];
    public string Decision { get; set; } = "";
    public string Outcome { get; set; } = "";
    public long DurationMs { get; set; }

    /// <summary>Readable one-liner for the Activity pane, e.g. "total = 0"; not written to the file.</summary>
    [JsonIgnore]
    public string Summary { get; set; } = "";
}

public static class AuditDecision
{
    public const string None = "";
    public const string Allow = "allow";
    public const string AllowOnce = "allowOnce";
    public const string AllowSession = "allowSession";
    public const string DeniedUser = "denied:user";
    public const string DeniedTimeout = "denied:timeout";
    public const string DeniedPolicy = "denied:policy";
    public const string DeniedCancelled = "denied:cancelled";
    public const string DeniedDisconnected = "denied:disconnected";
}

/// <summary>Output → "VsMcpDebugger Activity" in Visual Studio.</summary>
public interface IActivitySink
{
    void Write(string line);
}

/// <summary>
/// &lt;home&gt;/logs/audit-yyyyMMdd.log (UTC date), JSON lines, rolled at maxFileBytes, pruned by retention.
/// Never throws: an audit write failure is logged and the call goes on.
/// </summary>
public sealed class AuditLog(string directory, Func<int> retentionDays, IActivitySink activity, ILog log, Func<DateTimeOffset>? clock = null, long maxFileBytes = AuditLog.MaxFileBytes)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTime _prunedOn;

    public void Record(AuditEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        activity.Write(ActivityLine.Format(entry));

        var line = ProtocolJson.Serialize(entry) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(line);
        try
        {
            lock (_gate)
            {
                var now = _clock();
                Directory.CreateDirectory(directory);
                if (_prunedOn != now.UtcDateTime.Date)
                {
                    LogRetention.Prune(directory, "audit-", retentionDays(), now);
                    _prunedOn = now.UtcDateTime.Date;
                }

                File.AppendAllText(PathFor(now, bytes), line, Encoding.UTF8);
            }
        }
        catch (IOException e)
        {
            log.Warn($"Audit entry {entry.CallId} not written: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            log.Warn($"Audit entry {entry.CallId} not written: {e.Message}");
        }
    }

    private string PathFor(DateTimeOffset now, int lineBytes)
    {
        var stem = "audit-" + now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (var n = 0; ; n++)
        {
            var path = Path.Combine(directory, n == 0 ? stem + ".log" : $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}.log");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length + lineBytes <= maxFileBytes) return path;
        }
    }
}

public static class ActivityLine
{
    public static string Format(AuditEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        var time = entry.Ts.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (entry.Decision.Length == 0)
            return $"{time}  {entry.Operation}  {entry.Summary}".TrimEnd();

        var ms = entry.DurationMs.ToString(CultureInfo.InvariantCulture);
        return $"{time}  {entry.CallId}  {entry.Operation}  {entry.Summary}  → {Describe(entry.Decision)}  {entry.Outcome}  {ms} ms";
    }

    private static string Describe(string decision) => decision switch
    {
        AuditDecision.Allow => "allowed",
        AuditDecision.AllowOnce => "allowed (once)",
        AuditDecision.AllowSession => "allowed (session)",
        AuditDecision.DeniedUser => "denied by user",
        AuditDecision.DeniedTimeout => "denied (no answer)",
        AuditDecision.DeniedPolicy => "denied by settings",
        AuditDecision.DeniedCancelled => "cancelled",
        AuditDecision.DeniedDisconnected => "denied (AI disconnected)",
        _ => decision,
    };
}
