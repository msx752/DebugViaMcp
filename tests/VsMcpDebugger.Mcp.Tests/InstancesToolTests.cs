using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;
using Xunit;
using static VsMcpDebugger.Mcp.Tests.ExecutionToolsTests;

namespace VsMcpDebugger.Mcp.Tests;

public class InstancesToolTests
{
    [Fact]
    public async Task Instances_report_availability_holder_and_folder_match()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge())
        {
            WorkingDirectory = @"D:\mine\src",
            Instances =
            [
                new InstanceRecord { Pid = 1, SolutionPath = @"D:\mine\App.sln" },
                new InstanceRecord { Pid = 2, SolutionPath = @"D:\other\Job.sln", LeaseHolder = "codex", LeaseHolderFolder = @"D:\other" },
                new InstanceRecord { Pid = 3, SolutionPath = @"D:\third\T.sln" },
            ],
        };

        var json = Json(await new SessionTools(cm).Instances());
        var list = json.GetProperty("instances");

        Assert.Equal("free", list[0].GetProperty("availability").GetString());
        Assert.True(list[0].GetProperty("matchesFolder").GetBoolean());
        Assert.False(list[0].TryGetProperty("heldBy", out _));
        Assert.False(list[0].TryGetProperty("heldByFolder", out _));

        Assert.Equal("in_use", list[1].GetProperty("availability").GetString());
        Assert.Equal("codex", list[1].GetProperty("heldBy").GetString());
        Assert.Equal(@"D:\other", list[1].GetProperty("heldByFolder").GetString());
        Assert.False(list[1].GetProperty("matchesFolder").GetBoolean());

        Assert.Equal("free", list[2].GetProperty("availability").GetString());
        Assert.False(list[2].GetProperty("matchesFolder").GetBoolean());
    }
}
