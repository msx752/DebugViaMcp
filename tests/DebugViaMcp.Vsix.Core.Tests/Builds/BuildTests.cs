using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Builds;

public class BuildTests
{
    private static (DebugBridgeService Svc, FakeDte Dte) Create(DebuggerMode mode = DebuggerMode.Design)
    {
        var dte = new FakeDte { Mode = mode };
        return (new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), dte);
    }

    private static IEnumerable<string> Starts(FakeDte dte) => dte.Calls.Where(c => c.StartsWith("StartBuild:", StringComparison.Ordinal));

    [Fact]
    public async Task Solution_build_starts_and_reports_the_result()
    {
        var (svc, dte) = Create();

        var build = svc.BuildAsync(null, 60, CancellationToken.None);
        Assert.Equal(["StartBuild:"], Starts(dte));
        svc.Builds.OnBuildDone(failedProjects: 1);
        var r = await build;

        Assert.Equal(BuildState.Done, r.State);
        Assert.False(r.Succeeded);
        Assert.Equal(1, r.FailedProjects);
    }

    /// <summary>Review focus #5.</summary>
    [Fact]
    public async Task Running_build_is_joined_not_restarted()
    {
        var (svc, dte) = Create();
        dte.IsBuilding = true;          // the user pressed Ctrl+Shift+B

        var build = svc.BuildAsync(null, 60, CancellationToken.None);
        svc.Builds.OnBuildDone(0);

        Assert.True((await build).Succeeded);
        Assert.Empty(Starts(dte));
    }

    [Fact]
    public async Task Timeout_reports_building_and_the_next_call_joins()
    {
        var (svc, dte) = Create();

        var first = await svc.BuildAsync(null, 1, CancellationToken.None);
        var second = svc.BuildAsync(null, 60, CancellationToken.None);
        svc.Builds.OnBuildDone(0);

        Assert.Equal(BuildState.Building, first.State);
        Assert.Equal(BuildState.Done, (await second).State);
        Assert.Single(Starts(dte));
    }

    [Fact]
    public async Task Project_is_matched_by_name()
    {
        var (svc, dte) = Create();
        var build = svc.BuildAsync("faultdemo", 60, CancellationToken.None);
        svc.Builds.OnBuildDone(0);
        await build;
        Assert.Equal([@"StartBuild:samples\FaultDemo\FaultDemo.csproj"], Starts(dte));
    }

    [Fact]
    public async Task Unknown_project_is_not_found_with_the_project_names()
    {
        var (svc, _) = Create();
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.BuildAsync("Nope", 60, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
        Assert.Contains("OrderDemo", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_while_debugging_is_wrong_mode()
    {
        var (svc, dte) = Create(DebuggerMode.Break);
        var ex = await Assert.ThrowsAsync<BridgeException>(() => svc.BuildAsync(null, 60, CancellationToken.None));
        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
        Assert.Empty(Starts(dte));
    }
}
