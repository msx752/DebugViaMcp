using StreamJsonRpc;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class BuildFreshnessTests
{
    private const string Order = @"samples\OrderDemo\OrderDemo.csproj";
    private const string Fault = @"samples\FaultDemo\FaultDemo.csproj";
    private static readonly DateTime Noon = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static void MarkStale(FakeDte dte, string unique) =>
        dte.BuildInfos[unique] = new ProjectBuildInfo { UniqueName = unique, NewestSourceUtc = Noon.AddMinutes(5), OldestOutputUtc = Noon };

    private static DebugBridgeService ServiceFor(FakeDte dte) =>
        new(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0");

    [Fact]
    public async Task Named_project_is_checked()
    {
        var dte = new FakeDte();
        MarkStale(dte, Order);

        var warnings = await ServiceFor(dte).CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);

        var warning = Assert.Single(warnings);
        Assert.Equal(BuildWarningCodes.StaleBuild, warning.Code);
        Assert.Equal("OrderDemo", warning.Project);
    }

    /// <summary>Final review 4: repeated checks must not re-read the project every time.</summary>
    [Fact]
    public async Task Checks_within_a_few_seconds_read_the_project_once_and_a_build_refreshes_it()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var dte = new FakeDte();
        MarkStale(dte, Order);
        var service = ServiceFor(dte);
        service.BuildInfoClock = () => now;

        for (var i = 0; i < 20; i++) await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);
        Assert.Equal(1, dte.GetBuildInfoCalls);

        now = now.AddSeconds(DebugBridgeService.BuildInfoCacheSeconds + 1);
        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);
        Assert.Equal(2, dte.GetBuildInfoCalls);

        service.OnBuildFinished();
        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);
        Assert.Equal(3, dte.GetBuildInfoCalls);
    }

    [Fact]
    public async Task Unknown_project_gives_no_warning()
    {
        var dte = new FakeDte();
        MarkStale(dte, Order);

        Assert.Empty(await ServiceFor(dte).CheckBuildFreshnessAsync("Nope", null, CancellationToken.None));
    }

    [Fact]
    public async Task File_scope_checks_every_project_that_contains_it()
    {
        var dte = new FakeDte();
        MarkStale(dte, Order);
        MarkStale(dte, Fault);
        dte.FileProjects[@"C:\src\Demo\Shared\Util.cs"] = [Order, Fault];

        var warnings = await ServiceFor(dte).CheckBuildFreshnessAsync(null, @"Shared\Util.cs", CancellationToken.None);

        Assert.Equal(["FaultDemo", "OrderDemo"], warnings.Select(w => w.Project).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_file_outside_the_solution_is_never_looked_up()
    {
        var dte = new FakeDte();
        MarkStale(dte, Order);
        dte.FileProjects[@"D:\elsewhere\x.cs"] = [Order];

        var warnings = await ServiceFor(dte).CheckBuildFreshnessAsync(null, @"D:\elsewhere\x.cs", CancellationToken.None);

        Assert.Empty(warnings);
        Assert.Empty(dte.ProjectsContainingCalls);
    }

    [Fact]
    public async Task No_scope_checks_the_startup_project()
    {
        var dte = new FakeDte { StartupProject = Fault };
        MarkStale(dte, Order);
        MarkStale(dte, Fault);

        var warnings = await ServiceFor(dte).CheckBuildFreshnessAsync(null, null, CancellationToken.None);

        Assert.Equal("FaultDemo", Assert.Single(warnings).Project);
    }

    [Fact]
    public async Task No_scope_and_no_startup_project_gives_no_warning()
    {
        var dte = new FakeDte { StartupProject = null };
        MarkStale(dte, Order);

        Assert.Empty(await ServiceFor(dte).CheckBuildFreshnessAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Fresh_projects_give_an_empty_list()
    {
        var dte = new FakeDte();

        Assert.Empty(await ServiceFor(dte).CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None));
    }

    [Fact]
    public async Task Warning_text_names_the_project_and_carries_no_paths_or_values()
    {
        var dte = new FakeDte();
        MarkStale(dte, Order);

        var warning = Assert.Single(await ServiceFor(dte).CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None));

        Assert.StartsWith("OrderDemo", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Build or rebuild", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("C:", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_audits_nothing_for_a_read_tier_check_without_a_question()
    {
        using var s = new SecurityHarness();
        MarkStale(s.Dte, Order);
        await s.HandshakeAsync("claude-code");

        var warnings = await s.Handler.CheckBuildFreshnessAsync("OrderDemo", null, SecurityHarness.Ctx(), CancellationToken.None);

        Assert.Single(warnings);
        Assert.Empty(s.Prompt.Texts);
        Assert.DoesNotContain(s.AuditLines(), l => l.Contains("build_check", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handler_check_never_takes_the_lease_but_respects_it()
    {
        using var s = new SecurityHarness();
        await s.HandshakeAsync("claude-code");
        var other = await s.AddConnectionAsync("other-agent");

        await s.Handler.CheckBuildFreshnessAsync("OrderDemo", null, SecurityHarness.Ctx(), CancellationToken.None);
        await other.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, SecurityHarness.Ctx(), CancellationToken.None);   // the check did not take it

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() =>
            s.Handler.CheckBuildFreshnessAsync("OrderDemo", null, SecurityHarness.Ctx(), CancellationToken.None));   // the other agent holds it now
        Assert.Equal((int)BridgeErrorCode.InUse, ex.ErrorCode);
    }
}
