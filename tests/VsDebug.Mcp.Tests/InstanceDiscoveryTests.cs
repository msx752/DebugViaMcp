using VsDebug.Mcp.Discovery;
using VsDebug.Protocol;
using Xunit;

namespace VsDebug.Mcp.Tests;

public sealed class InstanceDiscoveryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vsdebug-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(int pid) => File.WriteAllText(
        Path.Combine(_dir, pid + ".json"),
        ProtocolJson.Serialize(new InstanceRecord { Pid = pid, PipeName = "vsdebug-" + pid, Token = "t" }));

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
        var discovery = new InstanceDiscovery(_dir, pid => pid == 10);

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
}
