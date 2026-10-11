using VsMcpDebugger.Mcp.Logging;
using Xunit;

namespace VsMcpDebugger.Mcp.Tests;

public sealed class DiagnosticsReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vsmcpdebugger-diag-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private string Write(string name, IEnumerable<string> lines, DateTime written)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void Reads_the_tail_of_the_newest_mcp_log()
    {
        Write("mcp-20261010.log", ["old"], new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        Write("mcp-20261011.log", Enumerable.Range(1, 100).Select(n => $"line {n}"), new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));
        Write("vsix-1.log", ["not mine"], new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));

        var (file, lines) = DiagnosticsReader.Tail(_dir, 3);

        Assert.Equal("mcp-20261011.log", file);
        Assert.Equal(["line 98", "line 99", "line 100"], lines);
    }

    [Fact]
    public void A_large_log_never_returns_a_partial_first_line()
    {
        Write("mcp-20261011.log", Enumerable.Range(0, 40_000).Select(n => n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture)), DateTime.UtcNow);

        var (_, lines) = DiagnosticsReader.Tail(_dir, DiagnosticsReader.MaxLines);

        Assert.Equal(DiagnosticsReader.MaxLines, lines.Length);
        Assert.All(lines, l => Assert.Equal(9, l.Length));
        Assert.Equal("000039999", lines[^1]);
    }

    [Fact]
    public void Missing_folder_or_log_is_empty()
    {
        var missingFolder = DiagnosticsReader.Tail(Path.Combine(_dir, "nope"), 10);
        Assert.Null(missingFolder.File);
        Assert.Empty(missingFolder.Lines);
        Assert.Empty(DiagnosticsReader.Tail(_dir, 10).Lines);
    }
}
