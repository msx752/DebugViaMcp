using VsDebug.Protocol;
using VsDebug.Vsix.Core;
using Xunit;

namespace VsDebug.Vsix.Core.Tests;

public sealed class DiscoveryWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vsdebug-dw-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Writes_record_named_by_pid_and_deletes_it()
    {
        var writer = new DiscoveryWriter(_dir);
        var (_, path) = writer.Write(new InstanceRecord { Pid = 4242, PipeName = "vsdebug-x", Token = "t" });

        Assert.Equal(Path.Combine(_dir, "4242.json"), path);
        var json = File.ReadAllText(path);
        Assert.Contains("\"pipeName\": \"vsdebug-x\"", json, StringComparison.Ordinal);   // camelCase, readable by non-.NET adapters
        Assert.Equal("vsdebug-x", ProtocolJson.Deserialize<InstanceRecord>(json)!.PipeName);

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
}
