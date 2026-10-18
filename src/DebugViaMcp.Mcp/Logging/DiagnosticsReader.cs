using System.Text;

namespace DebugViaMcp.Mcp.Logging;

/// <summary>Tail of this server's newest log file for the diag tool. The log never contains debuggee values.</summary>
public static class DiagnosticsReader
{
    public const int MaxLines = 500;

    /// <summary>Only the end of the file is read, so a 10 MB log costs no more than a small one.</summary>
    private const int MaxBytes = 256 * 1024;

    public static (string? File, string[] Lines) Tail(string logsDirectory, int lines)
    {
        lines = Math.Clamp(lines, 1, MaxLines);
        if (!Directory.Exists(logsDirectory)) return (null, []);
        var newest = new DirectoryInfo(logsDirectory).GetFiles("mcp-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (newest is null) return (null, []);

        // The server's own file sink has it open for writing.
        using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - MaxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var all = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (start > 0 && all.Count > 0) all.RemoveAt(0);                          // started mid-line
        if (all.Count > 0 && all[^1].Length == 0) all.RemoveAt(all.Count - 1);    // trailing newline
        return (newest.Name, all.TakeLast(lines).ToArray());
    }
}
