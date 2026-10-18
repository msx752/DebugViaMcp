using DebugViaMcp.Mcp.Discovery;
using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Mcp.Tests;

public sealed class InstanceDiscoveryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("debugviamcp-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(int pid) => File.WriteAllText(
        Path.Combine(_dir, pid + ".json"),
        ProtocolJson.Serialize(new InstanceRecord { Pid = pid, PipeName = "debugviamcp-" + pid, Token = "t" }));

    [Fact]
    public void Missing_directory_yields_empty_list()
    {
        var discovery = new InstanceDiscovery(Path.Combine(_dir, "nope"), _ => true);
        Assert.Empty(discovery.List());
    }

    [Fact]
    public void Live_instances_are_listed_ordered_by_pid()
    {
        Write(20);
        Write(10);
        var discovery = new InstanceDiscovery(_dir, _ => true);
        Assert.Equal([10, 20], discovery.List().Select(r => r.Pid));
    }

    [Fact]
    public void Dead_process_files_are_deleted()
    {
        Write(10);
        Write(20);
        var discovery = new InstanceDiscovery(_dir, r => r.Pid == 10);

        var list = discovery.List();

        Assert.Equal(10, Assert.Single(list).Pid);
        Assert.False(File.Exists(Path.Combine(_dir, "20.json")));
    }

    [Fact]
    public void Corrupt_files_are_skipped_not_thrown()
    {
        File.WriteAllText(Path.Combine(_dir, "30.json"), "{ not json");
        Write(10);
        var discovery = new InstanceDiscovery(_dir, _ => true);
        Assert.Equal(10, Assert.Single(discovery.List()).Pid);
    }

    /// <summary>Final review #1: protected processes throw Win32Exception from HasExited; that must mean "not ours".</summary>
    [Fact]
    public void Protected_process_pid_is_treated_as_stale_not_thrown()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "pid 4 is the Windows System process.");
        Assert.False(InstanceDiscovery.IsLive(new InstanceRecord { Pid = 4, StartedAt = DateTimeOffset.UtcNow }));
    }

    /// <summary>Final review #1: a pid reused by a process that started after the instance file was written is a ghost.</summary>
    [Fact]
    public void Reused_pid_started_after_the_record_is_treated_as_stale()
    {
        var self = Environment.ProcessId;
        Assert.False(InstanceDiscovery.IsLive(new InstanceRecord { Pid = self, StartedAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(InstanceDiscovery.IsLive(new InstanceRecord { Pid = self, StartedAt = DateTimeOffset.UtcNow.AddMinutes(1) }));
    }
}
