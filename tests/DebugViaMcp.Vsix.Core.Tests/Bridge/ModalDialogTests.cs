using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>
/// A dialog Visual Studio waits on (Hot Reload "changes not supported", "run the last successful build?") cannot be answered through the tools.
/// The extension says so instead of letting the agent wait: status names the dialog, and calls that would move the program fail at once.
/// </summary>
public class ModalDialogTests
{
    private const string Title = "Hot Reload - Changes not supported";
    private static readonly CallContext Ctx = new() { CallId = "c-test" };

    private static async Task<BridgeConnectionHandler> Connected(FakeDte dte, ILog? log = null)
    {
        var settings = new TestSettings();
        var security = new BridgeSecurity(settings, new ConsentGate(new FakePrompt(), () => settings.ConsentTimeout),
            new AuditLog(Path.Combine(Path.GetTempPath(), "debugviamcp-tests-audit"), () => 7, new NullActivity(), new NullLog()), new AiAccessSwitch(enabled: true));
        var h = new BridgeConnectionHandler(new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0"), "secret", security, log ?? new NullLog());
        await h.HandshakeAsync(new HandshakeRequest { Token = "secret", ClientWorkingDirectory = SecurityHarness.TrustedFolder, ProtocolMajor = ProtocolInfo.Major }, CancellationToken.None);
        return h;
    }

    private sealed class NullActivity : IActivitySink
    {
        public void Write(string line)
        {
        }
    }

    [Fact]
    public async Task Status_names_the_dialog_Visual_Studio_is_waiting_on()
    {
        var h = await Connected(new FakeDte { Dialog = Title });

        var status = await h.GetStatusAsync(Ctx, CancellationToken.None);

        Assert.Equal(Title, status.Dialog);
    }

    [Fact]
    public async Task Status_without_a_dialog_has_none()
    {
        var h = await Connected(new FakeDte());

        Assert.Null((await h.GetStatusAsync(Ctx, CancellationToken.None)).Dialog);
    }

    [Fact]
    public async Task A_dialog_title_is_cut_and_tidied_before_it_reaches_the_agent()
    {
        var h = await Connected(new FakeDte { Dialog = "Save\r\n changes   to " + new string('x', 500) });

        var dialog = (await h.GetStatusAsync(Ctx, CancellationToken.None)).Dialog!;

        Assert.True(dialog.Length <= DebugBridgeService.MaxDialogTitle, $"{dialog.Length} characters");
        Assert.DoesNotContain('\n', dialog);
        Assert.StartsWith("Save changes to ", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_window_that_is_blocked_without_a_title_is_still_reported()
    {
        var h = await Connected(new FakeDte { Dialog = "" });

        Assert.False(string.IsNullOrWhiteSpace((await h.GetStatusAsync(Ctx, CancellationToken.None)).Dialog));
    }

    [Fact]
    public async Task A_call_that_moves_the_program_fails_at_once_with_busy_and_does_not_touch_Visual_Studio()
    {
        var dte = new FakeDte { Dialog = Title };
        var h = await Connected(dte);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => h.ContinueAsync(Ctx, CancellationToken.None));

        Assert.Equal((int)BridgeErrorCode.Busy, ex.ErrorCode);
        Assert.Contains(Title, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Go", dte.Calls);
    }

    [Fact]
    public async Task Stepping_a_build_and_changing_a_value_are_held_back_too()
    {
        var dte = new FakeDte { Dialog = Title };
        var h = await Connected(dte);

        await Assert.ThrowsAsync<LocalRpcException>(() => h.StepAsync(StepKind.Over, Ctx, CancellationToken.None));
        await Assert.ThrowsAsync<LocalRpcException>(() => h.BuildAsync(null, 30, Ctx, CancellationToken.None));
        await Assert.ThrowsAsync<LocalRpcException>(() => h.SetValueAsync("total", "1", Ctx, CancellationToken.None));

        Assert.Empty(dte.Calls);
    }

    [Fact]
    public async Task Reads_still_work_while_a_dialog_is_open()
    {
        var h = await Connected(new FakeDte { Dialog = Title });

        var stack = await h.GetStackAsync(5, Ctx, CancellationToken.None);
        var vars = await h.GetVariablesAsync(VariableScope.Args, null, 0, 10, Ctx, CancellationToken.None);

        Assert.NotNull(stack);
        Assert.NotNull(vars);
    }

    [Fact]
    public async Task Without_a_dialog_nothing_changes()
    {
        var dte = new FakeDte();
        var h = await Connected(dte);

        await h.StepAsync(StepKind.Over, Ctx, CancellationToken.None);

        Assert.Contains("Step:Over", dte.Calls);
    }

    [Fact]
    public async Task Giving_up_the_hold_is_never_blocked()
    {
        var h = await Connected(new FakeDte { Dialog = Title });

        await h.ReleaseAsync(Ctx, CancellationToken.None);
    }

    [Fact]
    public async Task Reads_never_ask_Visual_Studio_about_a_dialog()
    {
        var dte = new FakeDte { Dialog = Title };
        var h = await Connected(dte);

        await h.GetStackAsync(5, Ctx, CancellationToken.None);
        await h.GetVariablesAsync(VariableScope.Args, null, 0, 10, Ctx, CancellationToken.None);

        Assert.Equal(0, dte.ModalDialogChecks);   // no extra UI-thread hop on the hot read path
    }

    [Fact]
    public async Task A_held_back_call_is_logged_with_its_call_id_and_the_dialog()
    {
        var log = new RecordingLog();
        var h = await Connected(new FakeDte { Dialog = Title }, log);

        await Assert.ThrowsAsync<LocalRpcException>(() => h.ContinueAsync(Ctx, CancellationToken.None));

        var line = Assert.Single(log.Warnings, w => w.Contains(Title, StringComparison.Ordinal));
        Assert.Contains("c-test", line, StringComparison.Ordinal);
        Assert.Contains("debug_continue", line, StringComparison.Ordinal);
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Warnings { get; } = [];
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) { }
    }
}
