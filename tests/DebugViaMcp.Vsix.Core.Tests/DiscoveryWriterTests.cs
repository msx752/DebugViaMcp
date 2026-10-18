using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests;

public sealed class DiscoveryWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "debugviamcp-dw-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Writes_record_named_by_pid_and_deletes_it()
    {
        var writer = new DiscoveryWriter(_dir);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "t" });

        Assert.Equal(Path.Combine(_dir, "4242.json"), path);
        var json = File.ReadAllText(path);
        Assert.Contains("\"pipeName\": \"debugviamcp-x\"", json, StringComparison.Ordinal);   // camelCase, readable by non-.NET adapters
        Assert.Equal("debugviamcp-x", ProtocolJson.Deserialize<InstanceRecord>(json)!.PipeName);

        writer.Delete();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Tokens_and_pipe_names_are_random_and_well_formed()
    {
        Assert.NotEqual(DiscoveryWriter.NewToken(), DiscoveryWriter.NewToken());
        Assert.True(DiscoveryWriter.NewToken().Length >= 43);
        Assert.StartsWith(ProtocolInfo.PipeNamePrefix, DiscoveryWriter.NewPipeName(), StringComparison.Ordinal);
    }

    /// <summary>Final review #9: the token must never sit in a file that is not yet hardened.</summary>
    [Fact]
    public void Write_hardens_a_temporary_file_before_it_becomes_visible()
    {
        var hardened = new List<(string Path, bool FinalExisted)>();
        var final = Path.Combine(_dir, "4242.json");
        var writer = new DiscoveryWriter(_dir, p => hardened.Add((p, File.Exists(final))));

        writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "secret" });

        var (path, finalExisted) = Assert.Single(hardened);
        Assert.NotEqual(final, path);
        Assert.False(finalExisted);
        Assert.Equal("secret", ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(final))!.Token);
        Assert.Equal(["4242.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void UpdateLease_writes_holder_and_folder_into_the_file()
    {
        var writer = new DiscoveryWriter(_dir);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "secret" });

        writer.UpdateLease("other-agent", @"D:\jobs");

        var record = ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(path))!;
        Assert.Equal("other-agent", record.LeaseHolder);
        Assert.Equal(@"D:\jobs", record.LeaseHolderFolder);
    }

    [Fact]
    public void Clearing_the_lease_removes_both_fields()
    {
        var writer = new DiscoveryWriter(_dir);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "secret" });
        writer.UpdateLease("other-agent", @"D:\jobs");

        writer.UpdateLease(null, null);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("leaseHolder", json, StringComparison.OrdinalIgnoreCase);
        var record = ProtocolJson.Deserialize<InstanceRecord>(json)!;
        Assert.Null(record.LeaseHolder);
        Assert.Null(record.LeaseHolderFolder);
    }

    [Fact]
    public void UpdateLease_keeps_every_other_field_and_the_token()
    {
        var hardenCount = 0;
        var writer = new DiscoveryWriter(_dir, _ => hardenCount++);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "secret", SolutionPath = @"C:\src\Shop.sln", VsVersion = "18.10" });

        writer.UpdateLease("other-agent", null);

        var record = ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(path))!;
        Assert.Equal("secret", record.Token);
        Assert.Equal("debugviamcp-x", record.PipeName);
        Assert.Equal(@"C:\src\Shop.sln", record.SolutionPath);
        Assert.Equal("18.10", record.VsVersion);
        Assert.Equal(4242, record.Pid);
        Assert.Equal(2, hardenCount);   // the rewrite goes through the same hardened temporary file
    }

    [Fact]
    public void UpdateLease_from_the_lease_publishes_the_current_holder_and_does_nothing_after_delete()
    {
        var lease = new InstanceLease();
        var writer = new DiscoveryWriter(_dir);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "secret" });

        lease.TryAcquire(1, "other-agent", null);
        writer.UpdateLease(lease);
        Assert.Equal("other-agent", ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(path))!.LeaseHolder);

        lease.Release(1);
        writer.UpdateLease(lease);
        Assert.Null(ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(path))!.LeaseHolder);

        writer.Delete();
        lease.TryAcquire(2, "late", null);
        writer.UpdateLease(lease);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void UpdateLease_before_write_does_nothing()
    {
        var writer = new DiscoveryWriter(_dir);

        writer.UpdateLease("other-agent", null);

        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    /// <summary>Final review #4: the solution shown by vs_instances must follow what is open in Visual Studio.</summary>
    [Fact]
    public void UpdateSolution_rewrites_the_record_and_keeps_the_token()
    {
        var hardenCount = 0;
        var writer = new DiscoveryWriter(_dir, _ => hardenCount++);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "debugviamcp-x", Token = "secret" });

        writer.UpdateSolution(@"C:\src\Shop.sln");
        var opened = ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(path))!;
        writer.UpdateSolution(null);
        var closed = ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(path))!;

        Assert.Equal(@"C:\src\Shop.sln", opened.SolutionPath);
        Assert.Null(closed.SolutionPath);
        Assert.Equal("secret", closed.Token);
        Assert.Equal(3, hardenCount);
    }
}
