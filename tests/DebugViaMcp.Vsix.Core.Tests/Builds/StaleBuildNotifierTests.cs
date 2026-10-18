using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Builds;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Builds;

public class StaleBuildNotifierTests
{
    private const string Order = @"samples\OrderDemo\OrderDemo.csproj";
    private const string Fault = @"samples\FaultDemo\FaultDemo.csproj";

    private sealed class FakeBuildPrompt : IBuildPrompt
    {
        public List<string> Shown { get; } = [];
        public List<string> Hidden { get; } = [];
        public Dictionary<string, Action> Actions { get; } = [];
        public Dictionary<string, string> Texts { get; } = [];

        public void Show(string project, string text, Action build)
        {
            Shown.Add(project);
            Texts[project] = text;
            Actions[project] = build;
        }

        public void Hide(string project) => Hidden.Add(project);
    }

    private sealed class Rig
    {
        public Rig() => Notifier = new StaleBuildNotifier(Prompt, () => Mode, project => Rebuilt.Add(project));

        public FakeBuildPrompt Prompt { get; } = new();
        public DebuggerMode Mode { get; set; } = DebuggerMode.Design;
        public List<string> Rebuilt { get; } = [];
        public StaleBuildNotifier Notifier { get; }
    }

    [Fact]
    public void Stale_project_shows_one_bar()
    {
        var r = new Rig();

        r.Notifier.Observe(Order, stale: true);

        Assert.Equal([Order], r.Prompt.Shown);
        Assert.Contains("OrderDemo", r.Prompt.Texts[Order], StringComparison.Ordinal);
        Assert.Contains("Build it now?", r.Prompt.Texts[Order], StringComparison.Ordinal);
    }

    [Fact]
    public void Twenty_observations_of_the_same_stale_project_show_one_bar()
    {
        var r = new Rig();

        for (var i = 0; i < 20; i++) r.Notifier.Observe(Order, stale: true);

        Assert.Single(r.Prompt.Shown);
    }

    [Fact]
    public void Two_stale_projects_show_two_bars()
    {
        var r = new Rig();

        r.Notifier.Observe(Order, true);
        r.Notifier.Observe(Fault, true);

        Assert.Equal([Order, Fault], r.Prompt.Shown);
    }

    [Fact]
    public void A_fresh_observation_hides_the_bar()
    {
        var r = new Rig();
        r.Notifier.Observe(Order, true);

        r.Notifier.Observe(Order, false);

        Assert.Equal([Order], r.Prompt.Hidden);
        Assert.Empty(r.Notifier.ShownProjects);
    }

    [Fact]
    public void A_fresh_observation_of_a_project_without_a_bar_hides_nothing()
    {
        var r = new Rig();

        r.Notifier.Observe(Order, false);

        Assert.Empty(r.Prompt.Hidden);
    }

    [Theory]
    [InlineData(DebuggerMode.Run)]
    [InlineData(DebuggerMode.Break)]
    public void No_bar_while_a_session_runs_or_is_stopped_at_a_breakpoint(DebuggerMode mode)
    {
        var r = new Rig { Mode = mode };

        r.Notifier.Observe(Order, true);

        Assert.Empty(r.Prompt.Shown);
    }

    [Fact]
    public void A_bar_that_is_up_when_a_session_starts_is_hidden_until_it_ends()
    {
        var r = new Rig();
        r.Notifier.Observe(Order, true);

        r.Mode = DebuggerMode.Run;
        r.Notifier.OnSessionStarted();

        Assert.Equal([Order], r.Prompt.Hidden);
        r.Mode = DebuggerMode.Design;
        r.Notifier.OnSessionEnded();
        Assert.Equal([Order, Order], r.Prompt.Shown);
    }

    [Fact]
    public void A_project_found_stale_during_a_session_gets_its_bar_when_the_session_ends()
    {
        var r = new Rig { Mode = DebuggerMode.Break };
        r.Notifier.Observe(Order, true);

        r.Mode = DebuggerMode.Design;
        r.Notifier.OnSessionEnded();

        Assert.Equal([Order], r.Prompt.Shown);
    }

    [Fact]
    public void A_project_that_became_fresh_during_the_session_gets_no_bar_afterwards()
    {
        var r = new Rig { Mode = DebuggerMode.Break };
        r.Notifier.Observe(Order, true);
        r.Notifier.Observe(Order, false);

        r.Mode = DebuggerMode.Design;
        r.Notifier.OnSessionEnded();

        Assert.Empty(r.Prompt.Shown);
    }

    [Fact]
    public void Session_end_while_still_not_in_design_mode_shows_nothing()
    {
        var r = new Rig { Mode = DebuggerMode.Break };
        r.Notifier.Observe(Order, true);

        r.Notifier.OnSessionEnded();

        Assert.Empty(r.Prompt.Shown);
    }

    [Fact]
    public void Dismissed_bar_is_not_shown_again_until_the_project_turns_fresh_and_stale_again()
    {
        var r = new Rig();
        r.Notifier.Observe(Order, true);

        r.Notifier.Dismissed(Order);
        r.Notifier.Observe(Order, true);
        r.Mode = DebuggerMode.Run;
        r.Mode = DebuggerMode.Design;
        r.Notifier.OnSessionEnded();
        Assert.Single(r.Prompt.Shown);

        r.Notifier.Observe(Order, false);
        r.Notifier.Observe(Order, true);
        Assert.Equal([Order, Order], r.Prompt.Shown);
    }

    [Fact]
    public void Pressing_Build_invokes_the_build_action_with_the_project_and_closes_the_bar()
    {
        var r = new Rig();
        r.Notifier.Observe(Order, true);

        r.Prompt.Actions[Order]();

        Assert.Equal([Order], r.Rebuilt);
        Assert.Equal([Order], r.Prompt.Hidden);
    }

    [Fact]
    public void A_project_that_is_still_stale_after_a_build_gets_its_bar_back()
    {
        var r = new Rig();
        r.Notifier.Observe(Order, true);
        r.Prompt.Actions[Order]();

        r.Notifier.Observe(Order, true);

        Assert.Equal([Order, Order], r.Prompt.Shown);
    }

    [Fact]
    public async Task Checking_freshness_feeds_the_notifier()
    {
        var prompt = new FakeBuildPrompt();
        var dte = new FakeDte { Mode = DebuggerMode.Design };
        dte.BuildInfos[Order] = new ProjectBuildInfo { UniqueName = Order, NewestSourceUtc = DateTime.UtcNow, OldestOutputUtc = DateTime.UtcNow.AddHours(-1) };
        var service = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0", buildPrompt: prompt);
        var now = DateTime.UtcNow;
        service.BuildInfoClock = () => now;

        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);
        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);

        Assert.Equal([Order], prompt.Shown);
        Assert.DoesNotContain(dte.Calls, c => c.StartsWith("StartBuild", StringComparison.Ordinal));

        dte.BuildInfos[Order] = new ProjectBuildInfo { UniqueName = Order, NewestSourceUtc = DateTime.UtcNow.AddHours(-2), OldestOutputUtc = DateTime.UtcNow };
        now = now.AddSeconds(DebugBridgeService.BuildInfoCacheSeconds + 1);
        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);

        Assert.Equal([Order], prompt.Hidden);
    }

    [Fact]
    public async Task A_finished_build_hides_the_bars_of_projects_that_are_up_to_date_now()
    {
        var prompt = new FakeBuildPrompt();
        var dte = new FakeDte { Mode = DebuggerMode.Design };
        dte.BuildInfos[Order] = new ProjectBuildInfo { UniqueName = Order, NewestSourceUtc = DateTime.UtcNow, OldestOutputUtc = DateTime.UtcNow.AddHours(-1) };
        var service = new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0", buildPrompt: prompt);
        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);

        dte.BuildInfos[Order] = new ProjectBuildInfo { UniqueName = Order, NewestSourceUtc = DateTime.UtcNow.AddHours(-2), OldestOutputUtc = DateTime.UtcNow };
        service.OnBuildFinished();

        Assert.Equal([Order], prompt.Hidden);
    }

    [Fact]
    public async Task Build_click_does_not_need_the_lease()
    {
        using var s = new SecurityHarness();
        var prompt = new FakeBuildPrompt();
        var service = new DebugBridgeService(s.Dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0", buildPrompt: prompt);
        s.Dte.Mode = DebuggerMode.Design;
        s.Dte.BuildInfos[Order] = new ProjectBuildInfo { UniqueName = Order, NewestSourceUtc = DateTime.UtcNow, OldestOutputUtc = DateTime.UtcNow.AddHours(-1) };
        await service.CheckBuildFreshnessAsync("OrderDemo", null, CancellationToken.None);
        await s.HandshakeAsync("claude-code");
        await s.Handler.SetBreakpointAsync(new BreakpointSpec { File = "OrderService.cs", Line = 9 }, SecurityHarness.Ctx(), CancellationToken.None);   // an agent holds the lease

        prompt.Actions[Order]();   // the owner's click goes straight to Visual Studio, not through the handler

        Assert.Contains("StartBuild:" + Order, s.Dte.Calls);
    }
}
