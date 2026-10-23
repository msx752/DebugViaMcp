using DebugViaMcp.TestSupport;
using Xunit;

namespace DebugViaMcp.Protocol.Tests;

/// <summary>A killed test host leaves its temp folders behind; the next test process sweeps what is ours and old.</summary>
public sealed class StaleTempSweepTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("debugviamcp-sweeptest-").FullName;
    private static readonly DateTime Old = DateTime.UtcNow.AddDays(-3);

    private string Make(string name, DateTime lastWriteUtc)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(Path.Combine(dir, "x.txt"), "x");
        Directory.SetLastWriteTimeUtc(dir, lastWriteUtc);
        return dir;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("debugviamcp-gate-abc.def")]
    [InlineData("debugviamcp-host-abc")]
    [InlineData("debugviamcp-diag-abc")]
    [InlineData("debugviamcp-test-abc")]
    [InlineData("debugviamcp-lr-0123456789abcdef")]
    [InlineData("debugviamcp-dw-0123456789abcdef")]
    [InlineData("debugviamcp-audit-abc")]
    [InlineData("debugviamcp-e2e-1234")]
    [InlineData("debugviamcp-check-1234")]
    [InlineData("debugviamcp-fake-profile")]
    [InlineData("dvm-deploy-0123456789abcdef")]
    public void An_old_folder_of_ours_is_removed(string name)
    {
        var dir = Make(name, Old);

        StaleTempSweep.Sweep(_root, DateTime.UtcNow.AddHours(-2));

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void A_fresh_folder_of_ours_stays_because_another_test_process_may_be_using_it()
    {
        var dir = Make("debugviamcp-gate-fresh", DateTime.UtcNow);

        StaleTempSweep.Sweep(_root, DateTime.UtcNow.AddHours(-2));

        Assert.True(Directory.Exists(dir));
    }

    [Theory]
    [InlineData("someone-elses-folder")]
    [InlineData("debugviamcp-gate")]
    [InlineData("debugviamcp-unknown-abc")]
    [InlineData("my-debugviamcp-gate-abc")]
    public void An_old_folder_that_is_not_ours_stays(string name)
    {
        var dir = Make(name, Old);

        StaleTempSweep.Sweep(_root, DateTime.UtcNow.AddHours(-2));

        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void The_shared_audit_folder_keeps_existing_but_loses_its_old_files()
    {
        var dir = Make("debugviamcp-tests-audit", DateTime.UtcNow);
        var oldFile = Path.Combine(dir, "audit-old.log");
        var newFile = Path.Combine(dir, "audit-new.log");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");
        File.SetLastWriteTimeUtc(oldFile, Old);

        StaleTempSweep.Sweep(_root, DateTime.UtcNow.AddHours(-2));

        Assert.True(Directory.Exists(dir));
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void A_folder_that_is_in_use_is_skipped_without_failing()
    {
        var dir = Make("debugviamcp-gate-locked", Old);
        using var held = new FileStream(Path.Combine(dir, "held.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Directory.SetLastWriteTimeUtc(dir, Old);

        var ex = Record.Exception(() => StaleTempSweep.Sweep(_root, DateTime.UtcNow.AddHours(-2)));

        Assert.Null(ex);
    }
}
