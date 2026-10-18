using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Protocol.Tests;

public sealed class LogRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "debugviamcp-lr-" + Guid.NewGuid().ToString("N"));

    public LogRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Make(string name, int ageDays)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
        return path;
    }

    [Fact]
    public void Old_files_with_the_prefix_are_deleted_and_recent_ones_stay()
    {
        var old = Make("mcp-20260101.log", 10);
        var recent = Make("mcp-20261016.log", 1);

        LogRetention.Prune(_dir, "mcp-", 7, DateTimeOffset.UtcNow);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Other_prefixes_and_other_extensions_are_never_touched()
    {
        var audit = Make("audit-20260101.log", 100);
        var text = Make("mcp-notes.txt", 100);

        LogRetention.Prune(_dir, "mcp-", 7, DateTimeOffset.UtcNow);

        Assert.True(File.Exists(audit));
        Assert.True(File.Exists(text));
    }

    [Fact]
    public void Fewer_than_one_day_still_keeps_today()
    {
        var today = Make("mcp-today.log", 0);

        LogRetention.Prune(_dir, "mcp-", 0, DateTimeOffset.UtcNow);

        Assert.True(File.Exists(today));
    }

    [Fact]
    public void A_missing_folder_is_not_an_error() =>
        LogRetention.Prune(Path.Combine(_dir, "does-not-exist"), "mcp-", 7, DateTimeOffset.UtcNow);
}
