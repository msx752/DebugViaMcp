using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

public class StartProjectTests
{
    private const string Order = @"samples\OrderDemo\OrderDemo.csproj";
    private const string Fault = @"samples\FaultDemo\FaultDemo.csproj";

    private static (FakeDte Dte, DebugBridgeService Service) Create(string? startup = Fault)
    {
        var dte = new FakeDte { Mode = DebuggerMode.Design, StartupProject = startup };
        dte.StartupHistory.Clear();
        return (dte, new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"));
    }

    [Fact]
    public async Task Named_project_is_started_with_the_startup_switched_for_the_launch()
    {
        var (dte, service) = Create();

        await service.StartProjectAsync("OrderDemo", CancellationToken.None);

        Assert.Equal([Order], dte.StartupAtGo);
        Assert.Contains("Go", dte.Calls);
    }

    [Fact]
    public async Task Startup_project_is_restored_after_success()
    {
        var (dte, service) = Create();

        await service.StartProjectAsync("OrderDemo", CancellationToken.None);

        Assert.Equal(Fault, dte.StartupProject);
        Assert.Equal([Order, Fault], dte.StartupHistory);
    }

    [Fact]
    public async Task Startup_project_is_restored_when_go_throws()
    {
        var (dte, service) = Create();
        dte.OnGo = () => throw new InvalidOperationException("launch failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartProjectAsync("OrderDemo", CancellationToken.None));

        Assert.Equal(Fault, dte.StartupProject);
    }

    [Fact]
    public async Task Startup_project_is_restored_when_the_call_is_cancelled_before_and_after_the_switch()
    {
        var (dte, service) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            await service.StartProjectAsync("OrderDemo", cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(Fault, dte.StartupProject);

        dte.OnGo = () => throw new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.StartProjectAsync("OrderDemo", CancellationToken.None));

        Assert.Equal(Fault, dte.StartupProject);
    }

    /// <summary>Final review 3: a solution with several startup projects keeps all of them.</summary>
    [Fact]
    public async Task Multiple_startup_projects_are_restored_whole()
    {
        var (dte, service) = Create();
        dte.StartupProjects = [Fault, Order];

        await service.StartProjectAsync("OrderDemo", CancellationToken.None);

        Assert.Equal([Fault, Order], dte.StartupProjects);
    }

    [Fact]
    public async Task Unknown_project_is_not_found_and_lists_the_names()
    {
        var (dte, service) = Create();

        var ex = await Assert.ThrowsAsync<BridgeException>(() => service.StartProjectAsync("Nope", CancellationToken.None));

        Assert.Equal(BridgeErrorCode.NotFound, ex.Code);
        Assert.Contains("OrderDemo", ex.Hint, StringComparison.Ordinal);
        Assert.Contains("FaultDemo", ex.Hint, StringComparison.Ordinal);
        Assert.Empty(dte.StartupHistory);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Fact]
    public async Task Project_that_cannot_launch_is_invalid_argument_naming_the_launchable_ones()
    {
        var (dte, service) = Create();
        dte.LaunchableProjects = [Fault];

        var ex = await Assert.ThrowsAsync<BridgeException>(() => service.StartProjectAsync("OrderDemo", CancellationToken.None));

        Assert.Equal(BridgeErrorCode.InvalidArgument, ex.Code);
        Assert.Contains("FaultDemo", ex.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderDemo", ex.Hint, StringComparison.Ordinal);
        Assert.Empty(dte.StartupHistory);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Fact]
    public async Task Break_mode_is_wrong_mode_and_nothing_changes()
    {
        var (dte, service) = Create();
        dte.Mode = DebuggerMode.Break;

        var ex = await Assert.ThrowsAsync<BridgeException>(() => service.StartProjectAsync("OrderDemo", CancellationToken.None));

        Assert.Equal(BridgeErrorCode.WrongMode, ex.Code);
        Assert.Empty(dte.StartupHistory);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Theory]
    [InlineData("orderdemo")]
    [InlineData(@"samples\OrderDemo\OrderDemo.csproj")]
    [InlineData(@"SAMPLES\ORDERDEMO\ORDERDEMO.CSPROJ")]
    public async Task Project_name_is_matched_case_insensitively_by_name_or_unique_name(string name)
    {
        var (dte, service) = Create();

        await service.StartProjectAsync(name, CancellationToken.None);

        Assert.Equal([Order], dte.StartupAtGo);
    }

    [Fact]
    public async Task No_startup_project_before_means_it_is_cleared_again()
    {
        var (dte, service) = Create(startup: null);

        await service.StartProjectAsync("OrderDemo", CancellationToken.None);

        Assert.Null(dte.StartupProject);
        Assert.Equal([Order, null], dte.StartupHistory);
    }

    [Fact]
    public async Task Start_project_returns_the_sequence_observed_before_the_launch()
    {
        var (_, service) = Create();

        var seq = await service.StartProjectAsync("OrderDemo", CancellationToken.None);

        Assert.Equal(0, seq);
    }

    [Fact]
    public async Task Start_project_takes_the_lease_and_is_audited_with_the_project_name()
    {
        using var s = new SecurityHarness();
        s.Dte.Mode = DebuggerMode.Design;
        await s.HandshakeAsync("claude-code");
        var other = await s.AddConnectionAsync("other-agent");

        await s.Handler.StartProjectAsync("OrderDemo", SecurityHarness.Ctx("c-000042"), CancellationToken.None);

        Assert.Contains(s.AuditLines(), l => l.Contains("c-000042", StringComparison.Ordinal) && l.Contains("\"operation\":\"debug_start\"", StringComparison.Ordinal)
            && l.Contains("\"project\":\"OrderDemo\"", StringComparison.Ordinal));
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => other.StartProjectAsync("FaultDemo", SecurityHarness.Ctx(), CancellationToken.None));
        Assert.Equal((int)BridgeErrorCode.InUse, ex.ErrorCode);
    }
}
