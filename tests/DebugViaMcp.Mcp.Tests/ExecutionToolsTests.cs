using System.Text.Json;
using ModelContextProtocol.Protocol;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Mcp.Tests;

public class ExecutionToolsTests
{
    internal static JsonElement Json(CallToolResult r) => JsonDocument.Parse(((TextContentBlock)r.Content[0]).Text).RootElement;

    private static BreakEvent Break(long seq) => new()
    {
        Seq = seq,
        Snapshot = new BreakSnapshot
        {
            Reason = BreakReason.Step,
            Location = new SourceLocation { File = "OrderService.cs", Line = 9, Function = "OrderService.Submit" },
        },
    };

    [Fact]
    public async Task Start_with_a_project_calls_start_project()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var result = await tools.Start("OrderDemo", CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("StartProject:OrderDemo", cm.Bridge.Calls);
        Assert.DoesNotContain("Start", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Start_without_a_project_calls_the_plain_start()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        await tools.Start(null, CancellationToken.None);
        await tools.Start("  ", CancellationToken.None);

        Assert.Equal(2, cm.Bridge.Calls.Count(c => c == "Start"));
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("StartProject", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_with_a_project_against_a_2_1_extension_is_unsupported()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design) { RemoteProtocolMinor = ProtocolInfo.MinorM3 };
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Start("OrderDemo", CancellationToken.None));

        Assert.Equal("unsupported", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("Start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_project_not_found_is_returned_as_not_found()
    {
        var bridge = new FakeDebugBridge { OnStartProject = _ => throw new McpBridgeException(BridgeErrorCode.NotFound, "No project named 'Nope' in the solution.", "Projects: OrderDemo, FaultDemo") };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var error = Json(await tools.Start("Nope", CancellationToken.None)).GetProperty("error");

        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.Contains("OrderDemo", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    private static BuildWarning Stale(string? project = "OrderDemo") => new() { Code = "stale_build", Message = "OrderDemo — sources are newer than the built output.", Project = project };

    [Fact]
    public async Task Start_reports_stale_build_warnings_and_still_starts()
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => [Stale()] };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design);
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Start(null, CancellationToken.None));

        var warning = json.GetProperty("warnings")[0];
        Assert.Equal("stale_build", warning.GetProperty("code").GetString());
        Assert.Equal("OrderDemo", warning.GetProperty("project").GetString());
        Assert.Contains("Start", bridge.Calls);
        Assert.True(bridge.Calls.IndexOf("CheckBuild:") < bridge.Calls.IndexOf("Start"));
    }

    [Fact]
    public async Task Start_without_warnings_has_no_warnings_property()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Start(null, CancellationToken.None));

        Assert.False(json.TryGetProperty("warnings", out _));
        Assert.Equal("running", json.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Start_with_a_project_checks_that_project()
    {
        var bridge = new FakeDebugBridge();
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        await tools.Start("OrderDemo", CancellationToken.None);

        Assert.Contains("CheckBuild:OrderDemo", bridge.Calls);
    }

    [Fact]
    public async Task Attach_by_name_checks_that_name()
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => [Stale()] };
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Attach(null, "OrderDemo", CancellationToken.None));

        Assert.Contains("CheckBuild:OrderDemo", bridge.Calls);
        Assert.Equal("stale_build", json.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Equal("attached", json.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Attach_by_pid_checks_the_name_of_that_process()
    {
        var bridge = new FakeDebugBridge();
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        await tools.Attach(400, null, CancellationToken.None);

        Assert.Contains("CheckBuild:OrderDemo", bridge.Calls);
    }

    [Fact]
    public async Task Attach_by_pid_of_an_unlisted_process_has_no_name_to_check()
    {
        var bridge = new FakeDebugBridge { OnListAttachable = _ => [] };
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        await tools.Attach(999, null, CancellationToken.None);

        Assert.DoesNotContain(bridge.Calls, c => c.StartsWith("CheckBuild", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_is_skipped_against_a_2_1_extension()
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => [Stale()] };
        var cm = new FakeConnectionManager(bridge, DebuggerMode.Design) { RemoteProtocolMinor = ProtocolInfo.MinorM3 };
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Start(null, CancellationToken.None));

        Assert.DoesNotContain(bridge.Calls, c => c.StartsWith("CheckBuild", StringComparison.Ordinal));
        Assert.False(json.TryGetProperty("warnings", out _));
    }

    [Fact]
    public async Task A_failing_check_does_not_fail_the_tool()
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => throw new StreamJsonRpc.RemoteInvocationException("boom", (int)BridgeErrorCode.Internal, new BridgeErrorData()) };
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        var result = await tools.Start(null, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Start", bridge.Calls);
    }

    /// <summary>Final review 5: a refused approval must end the call, not be asked a second time by the real command.</summary>
    [Theory]
    [InlineData(BridgeErrorCode.Denied, "denied")]
    [InlineData(BridgeErrorCode.AiDisabled, "ai_disabled")]
    public async Task Denied_or_disconnected_from_the_check_is_the_tools_result(BridgeErrorCode code, string name)
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => throw new StreamJsonRpc.RemoteInvocationException("no", (int)code, new BridgeErrorData()) };
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Start(null, CancellationToken.None));

        Assert.Equal(name, json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Start", bridge.Calls);
    }

    [Fact]
    public async Task In_use_from_the_check_is_the_tools_result()
    {
        var bridge = new FakeDebugBridge { OnCheckBuild = (_, _) => throw new StreamJsonRpc.RemoteInvocationException("This Visual Studio is in use by other-agent.", (int)BridgeErrorCode.InUse, new BridgeErrorData()) };
        var tools = new ExecutionTools(new FakeConnectionManager(bridge, DebuggerMode.Design), TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Start(null, CancellationToken.None));

        Assert.Equal("in_use", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Start", bridge.Calls);
    }

    [Fact]
    public async Task Step_returns_snapshot_of_next_break()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Bridge.OnStep = kind => { _ = Task.Run(() => cm.Session.OnBreakAsync(Break(11))); return 10; };
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var result = await tools.Step("over", CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var json = Json(result);
        Assert.Equal("break", json.GetProperty("state").GetString());
        Assert.Equal(9, json.GetProperty("snapshot").GetProperty("location").GetProperty("line").GetInt32());
        Assert.Contains("Step:Over", cm.Bridge.Calls);
    }

    [Fact]
    public async Task Step_while_running_is_wrong_mode_with_hint()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Run);
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var result = await tools.Step("over", CancellationToken.None);

        Assert.True(result.IsError);
        var error = Json(result).GetProperty("error");
        Assert.Equal("wrong_mode", error.GetProperty("code").GetString());
        Assert.Contains("debug_pause", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("Step", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Continue_times_out_to_running_state()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));

        var json = Json(await tools.Continue(CancellationToken.None));

        Assert.Equal("running", json.GetProperty("state").GetString());
        Assert.Contains("debug_wait", json.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Continue_returns_ended_when_session_ends()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Bridge.OnContinue = () =>
        {
            _ = Task.Run(() => cm.Session.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 11 }));
            return 10;
        };
        var tools = new ExecutionTools(cm, TimeSpan.FromSeconds(30));

        var started = DateTime.UtcNow;
        var json = Json(await tools.Continue(CancellationToken.None));

        Assert.Equal("design", json.GetProperty("state").GetString());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Invalid_step_kind_is_invalid_argument()
    {
        var tools = new ExecutionTools(new FakeConnectionManager(new FakeDebugBridge()), ExecutionTools.DefaultWait);

        var result = await tools.Step("sideways", CancellationToken.None);

        Assert.Equal("invalid_argument", Json(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Wait_caps_timeout_at_120_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), ExecutionTools.ClampWait(999));
        Assert.Equal(TimeSpan.FromSeconds(1), ExecutionTools.ClampWait(0));
    }

    [Fact]
    public async Task Bridge_failure_is_reported_as_error_code()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge())
        {
            FailWith = new McpBridgeException(BridgeErrorCode.NotConnected, "gone", "run doctor"),
        };
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var error = Json(await tools.Step("over", CancellationToken.None)).GetProperty("error");

        Assert.Equal("not_connected", error.GetProperty("code").GetString());
        Assert.Equal("run doctor", error.GetProperty("hint").GetString());
    }

    /// <summary>Final review #2: continue times out, the break lands, then debug_wait must return it, not "running".</summary>
    [Fact]
    public async Task Wait_after_continue_timeout_returns_the_break_that_arrived_in_between()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        var tools = new ExecutionTools(cm, TimeSpan.FromMilliseconds(50));
        Assert.Equal("running", Json(await tools.Continue(CancellationToken.None)).GetProperty("state").GetString());

        await cm.Session.OnBreakAsync(Break(11));
        var started = DateTime.UtcNow;
        var json = Json(await tools.Wait(timeoutSec: 30, CancellationToken.None));

        Assert.Equal("break", json.GetProperty("state").GetString());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Snapshot_shows_the_epoch_for_frame_select()
    {
        var json = Json(ToolResponse.Ok(ToolResponse.Snapshot(new BreakSnapshot { Reason = BreakReason.Step, Epoch = 12 })!));
        Assert.Equal(12, json.GetProperty("epoch").GetInt64());
    }

    [Fact]
    public async Task Run_to_waits_for_the_break()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Bridge.OnRunTo = (_, _) => { _ = Task.Run(() => cm.Session.OnBreakAsync(Break(11))); return new RunToResult { Seq = 10 }; };
        var tools = new ExecutionTools(cm, ExecutionTools.DefaultWait);

        var json = Json(await tools.RunTo("OrderService.cs", 9, CancellationToken.None));

        Assert.Equal("break", json.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Run_to_an_unbindable_line_does_not_wait()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge());
        cm.Bridge.OnRunTo = (_, _) => new RunToResult { BindError = "No executable code at this location." };

        var json = Json(await new ExecutionTools(cm, ExecutionTools.DefaultWait).RunTo("OrderService.cs", 2, CancellationToken.None));

        Assert.Equal("No executable code at this location.", json.GetProperty("bindError").GetString());
        Assert.Equal("break", json.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Attach_by_name_resolves_a_single_candidate()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);

        var json = Json(await new ExecutionTools(cm).Attach(processName: "OrderDemo", ct: CancellationToken.None));

        Assert.Equal(400, json.GetProperty("pid").GetInt32());
        Assert.Contains("Attach:400", cm.Bridge.Calls);
    }

    /// <summary>E2E: debug_wait right after debug_attach returned "session ended" because attach never set the resume point.</summary>
    [Fact]
    public async Task Attach_sets_the_resume_point_to_the_sequence_it_observed()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge { Seq = 12 }, DebuggerMode.Design);
        await cm.Session.OnModeChangedAsync(new ModeChangedEvent { Mode = DebuggerMode.Design, Seq = 8 });

        await new ExecutionTools(cm).Attach(processName: "OrderDemo", ct: CancellationToken.None);

        Assert.Equal(12, cm.Session.LastResumeSeq);
    }

    [Fact]
    public async Task Attach_by_ambiguous_name_lists_candidates_and_attaches_nothing()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design);
        cm.Bridge.OnListAttachable = _ => [new ProcessInfo { Pid = 100, Name = "FaultDemo" }, new ProcessInfo { Pid = 200, Name = "FaultDemo" }];

        var error = Json(await new ExecutionTools(cm).Attach(processName: "FaultDemo", ct: CancellationToken.None)).GetProperty("error");

        Assert.Equal("invalid_argument", error.GetProperty("code").GetString());
        Assert.Contains("100 FaultDemo, 200 FaultDemo", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(cm.Bridge.Calls, c => c.StartsWith("Attach:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attach_needs_exactly_one_of_pid_and_name()
    {
        var tools = new ExecutionTools(new FakeConnectionManager(new FakeDebugBridge(), DebuggerMode.Design));
        var error = Json(await tools.Attach(pid: 1, processName: "x", ct: CancellationToken.None)).GetProperty("error");
        Assert.Equal("invalid_argument", error.GetProperty("code").GetString());
    }
}
