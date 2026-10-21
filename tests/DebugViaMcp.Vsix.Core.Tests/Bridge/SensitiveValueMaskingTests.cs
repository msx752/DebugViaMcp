using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Tests.Fakes;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Bridge;

/// <summary>Every path that carries program values to the agent hides secrets, unless the user turned the mask off.</summary>
public class SensitiveValueMaskingTests
{
    private const string Mask = SensitiveValueMask.Placeholder;

    private static (DebugBridgeService Svc, FakeDte Dte) Create(Func<bool>? maskSensitiveValues = null)
    {
        var dte = new FakeDte { Mode = DebuggerMode.Break };
        var svc = maskSensitiveValues is null
            ? new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0")
            : new DebugBridgeService(dte, new InlineUiThread(), new EventSeq(), new NullLog(), "0.1.0", maskSensitiveValues: maskSensitiveValues);
        return (svc, dte);
    }

    private static VariableInfo[] SomeLocals() =>
    [
        new() { Name = "password", Type = "string", Value = "\"hunter2\"" },
        new() { Name = "total", Type = "decimal", Value = "1200" },
    ];

    [Fact]
    public async Task Variables_under_a_sensitive_name_are_masked_and_flagged()
    {
        var (svc, dte) = Create();
        dte.Locals = SomeLocals().ToList();

        var vars = await svc.GetVariablesAsync(VariableScope.Locals, null, 0, 10, CancellationToken.None);

        Assert.Equal(Mask, vars[0].Value);
        Assert.True(vars[0].Masked);
        Assert.Equal("1200", vars[1].Value);
        Assert.Null(vars[1].Masked);
    }

    [Fact]
    public async Task Evaluating_a_sensitive_expression_is_masked_and_flagged()
    {
        var (svc, dte) = Create();
        dte.OnEvaluate = _ => new EvaluateResult { IsValid = true, Value = "\"hunter2\"", Type = "string" };

        var r = await svc.EvaluateAsync("config.Password", allowSideEffects: false, CancellationToken.None);

        Assert.Equal(Mask, r.Value);
        Assert.True(r.Masked);
    }

    [Fact]
    public async Task Exception_text_with_a_secret_is_masked_everywhere_it_appears()
    {
        var (svc, dte) = Create();
        dte.ThrownException = new RawExceptionInfo
        {
            Type = "System.Exception",
            Message = "Login failed. Password=hunter2",
            StackTrace = "at Db.Open() Password=hunter2",
            Inner = [("System.InvalidOperationException", "Token=abc123")],
        };

        var e = (await svc.GetExceptionAsync(CancellationToken.None))!;

        Assert.Equal($"Login failed. Password={Mask}", e.Message);
        Assert.Equal($"at Db.Open() Password={Mask}", e.StackTrace);
        Assert.Equal($"Token={Mask}", e.Inner[0].Message);
        Assert.True(e.Masked);
    }

    [Fact]
    public async Task Output_lines_with_a_secret_are_masked()
    {
        var (svc, dte) = Create();
        dte.OutputLineText = n => n == 2 ? "connecting with Password=hunter2" : "line " + n;

        var page = await svc.ReadOutputAsync("build", 3, 0, CancellationToken.None);

        Assert.Contains($"connecting with Password={Mask}", page.Lines);
        Assert.DoesNotContain(page.Lines, l => l.Contains("hunter2"));
        Assert.True(page.Masked);
    }

    [Fact]
    public void The_arguments_of_the_break_snapshot_are_masked()
    {
        var (svc, dte) = Create();
        dte.Locals = SomeLocals().ToList();

        var snapshot = svc.BuildSnapshot(BreakReason.Breakpoint);

        Assert.Equal(Mask, snapshot.Args[0].Value);
        Assert.True(snapshot.Args[0].Masked);
    }

    [Fact]
    public async Task Nothing_is_masked_when_the_setting_is_off()
    {
        var (svc, dte) = Create(() => false);
        dte.Locals = SomeLocals().ToList();
        dte.ThrownException = new RawExceptionInfo { Type = "E", Message = "Password=hunter2" };

        var vars = await svc.GetVariablesAsync(VariableScope.Locals, null, 0, 10, CancellationToken.None);
        var e = (await svc.GetExceptionAsync(CancellationToken.None))!;

        Assert.Equal("\"hunter2\"", vars[0].Value);
        Assert.Null(vars[0].Masked);
        Assert.Equal("Password=hunter2", e.Message);
        Assert.Null(e.Masked);
    }

    [Fact]
    public async Task The_setting_is_read_on_every_call()
    {
        var on = true;
        var (svc, dte) = Create(() => on);
        dte.Locals = SomeLocals().ToList();

        var masked = await svc.GetVariablesAsync(VariableScope.Locals, null, 0, 10, CancellationToken.None);
        on = false;
        var plain = await svc.GetVariablesAsync(VariableScope.Locals, null, 0, 10, CancellationToken.None);

        Assert.Equal(Mask, masked[0].Value);
        Assert.Equal("\"hunter2\"", plain[0].Value);
    }
}
