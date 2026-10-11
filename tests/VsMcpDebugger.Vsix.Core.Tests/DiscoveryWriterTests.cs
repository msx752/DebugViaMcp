using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public sealed class DiscoveryWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vsmcpdebugger-dw-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Writes_record_named_by_pid_and_deletes_it()
    {
        var writer = new DiscoveryWriter(_dir);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "vsmcpdebugger-x", Token = "t" });

        Assert.Equal(Path.Combine(_dir, "4242.json"), path);
        var json = File.ReadAllText(path);
        Assert.Contains("\"pipeName\": \"vsmcpdebugger-x\"", json, StringComparison.Ordinal);   // camelCase, readable by non-.NET adapters
        Assert.Equal("vsmcpdebugger-x", ProtocolJson.Deserialize<InstanceRecord>(json)!.PipeName);

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

        writer.Write(new InstanceRecord { Pid = 4242, PipeName = "vsmcpdebugger-x", Token = "secret" });

        var (path, finalExisted) = Assert.Single(hardened);
        Assert.NotEqual(final, path);
        Assert.False(finalExisted);
        Assert.Equal("secret", ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(final))!.Token);
        Assert.Equal(["4242.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    /// <summary>Final review #4: the solution shown by vs_instances must follow what is open in Visual Studio.</summary>
    [Fact]
    public void UpdateSolution_rewrites_the_record_and_keeps_the_token()
    {
        var hardenCount = 0;
        var writer = new DiscoveryWriter(_dir, _ => hardenCount++);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "vsmcpdebugger-x", Token = "secret" });

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
