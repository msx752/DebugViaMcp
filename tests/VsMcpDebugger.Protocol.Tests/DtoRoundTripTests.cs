using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Protocol.Tests;

/// <summary>DTOs must survive a round trip through the wire conventions unchanged.</summary>
public class DtoRoundTripTests
{
    private static T RoundTrip<T>(T value) => ProtocolJson.Deserialize<T>(ProtocolJson.Serialize(value!))!;

    /// <summary>Golden test: this exact JSON is what a non-.NET adapter (e.g. the TypeScript VS Code adapter) sees.</summary>
    [Fact]
    public void Wire_format_is_camelCase_with_string_enums_and_no_nulls()
    {
        Assert.Equal("{\"mode\":\"break\",\"seq\":3}", ProtocolJson.Serialize(new ModeChangedEvent { Mode = DebuggerMode.Break, Seq = 3 }));
        Assert.Equal(
            "{\"reason\":\"breakpoint\",\"source\":[],\"args\":[],\"stackTop\":[],\"epoch\":0}",
            ProtocolJson.Serialize(new BreakSnapshot { Reason = BreakReason.Breakpoint }));
    }

    [Fact]
    public void Call_context_and_error_data_wire_format()
    {
        Assert.Equal("{\"callId\":\"c-000001\"}", ProtocolJson.Serialize(new CallContext { CallId = "c-000001" }));
        Assert.Equal("{\"hint\":\"h\",\"reason\":\"timeout\"}", ProtocolJson.Serialize(new BridgeErrorData { Hint = "h", Reason = DeniedReasons.Timeout }));
        Assert.Equal("{\"hint\":\"h\"}", ProtocolJson.Serialize(new BridgeErrorData { Hint = "h" }));
    }

    [Fact]
    public void Permission_set_wire_format()
    {
        Assert.Equal("{\"read\":\"allow\",\"control\":\"allow\",\"mutate\":\"ask\"}", ProtocolJson.Serialize(new PermissionSet()));
    }

    [Fact]
    public void BreakEvent_round_trips()
    {
        var original = new BreakEvent
        {
            Seq = 7,
            Snapshot = new BreakSnapshot
            {
                Reason = BreakReason.Breakpoint,
                BreakpointId = "bp-1a2b3c4d",
                Location = new SourceLocation { File = "src/A.cs", Line = 42, Function = "A.M(int x)" },
                Source = ["41: a", "> 42: b"],
                Args = [new VariableInfo { Name = "x", Type = "int", Value = "3" }],
                StackTop = ["A.M:42"],
            },
        };

        var copy = RoundTrip(original);

        Assert.Equal(7, copy.Seq);
        Assert.Equal(BreakReason.Breakpoint, copy.Snapshot.Reason);
        Assert.Equal("src/A.cs", copy.Snapshot.Location!.File);
        Assert.Equal(42, copy.Snapshot.Location.Line);
        Assert.Equal("x", Assert.Single(copy.Snapshot.Args).Name);
        Assert.Equal(["41: a", "> 42: b"], copy.Snapshot.Source);
    }

    [Fact]
    public void InstanceRecord_round_trips()
    {
        var original = new InstanceRecord
        {
            Pid = 1234, PipeName = "vsmcpdebugger-abc", Token = "t", SolutionPath = @"C:\s\a.sln",
            VsVersion = "18.10", VsixVersion = "0.1.0", ProtocolMajor = 1, ProtocolMinor = 0,
            StartedAt = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero),
        };

        var copy = RoundTrip(original);

        Assert.Equal(original.Pid, copy.Pid);
        Assert.Equal(original.PipeName, copy.PipeName);
        Assert.Equal(original.SolutionPath, copy.SolutionPath);
        Assert.Equal(original.StartedAt, copy.StartedAt);
    }

    [Theory]
    [InlineData(BridgeErrorCode.NotConnected, "not_connected")]
    [InlineData(BridgeErrorCode.AmbiguousInstance, "ambiguous_instance")]
    [InlineData(BridgeErrorCode.WrongMode, "wrong_mode")]
    [InlineData(BridgeErrorCode.VersionMismatch, "version_mismatch")]
    [InlineData(BridgeErrorCode.EvalFailed, "eval_failed")]
    public void Error_codes_have_stable_snake_case_names(BridgeErrorCode code, string name)
        => Assert.Equal(name, BridgeErrorNames.ToName(code));

    [Fact]
    public void Run_to_reason_is_camel_case_on_the_wire()
    {
        Assert.Contains("\"reason\":\"runTo\"", ProtocolJson.Serialize(new BreakSnapshot { Reason = BreakReason.RunTo }), StringComparison.Ordinal);
    }
}
