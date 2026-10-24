using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Protocol.Tests;

/// <summary>DTOs must survive a round trip through the wire conventions unchanged.</summary>
public class DtoRoundTripTests
{
    private static T RoundTrip<T>(T value) => ProtocolJson.Deserialize<T>(ProtocolJson.Serialize(value!))!;

    /// <summary>Golden test: this exact JSON is what a non-.NET adapter (for example a client written in another language) sees.</summary>
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
    public void Symbol_status_wire_format_leaves_out_options_it_does_not_know()
    {
        Assert.Equal(
            "{\"justMyCode\":true,\"unbound\":[],\"notes\":[]}",
            ProtocolJson.Serialize(new SymbolStatus { JustMyCode = true }));
    }

    [Fact]
    public void Symbol_status_round_trips()
    {
        var original = new SymbolStatus
        {
            RequireSourceToMatch = true, SourceServer = false,
            Unbound = [new BreakpointInfo { Id = "bp-1", File = "A.cs", Line = 3, BindError = "x" }],
            Notes = ["n"],
        };

        var copy = RoundTrip(original);

        Assert.Equal(true, copy.RequireSourceToMatch);
        Assert.Equal(false, copy.SourceServer);
        Assert.Null(copy.JustMyCode);
        Assert.Equal(3, copy.Unbound[0].Line);
        Assert.Equal(["n"], copy.Notes);
    }

    [Fact]
    public void Exception_settings_wire_format()
    {
        Assert.Equal(
            "{\"group\":\"CLR\",\"changes\":[{\"name\":\"A\",\"previous\":false,\"current\":true,\"changed\":true}]}",
            ProtocolJson.Serialize(new ExceptionSetResult
            {
                Group = "CLR", Changes = [new ExceptionChange { Name = "A", Previous = false, Current = true, Changed = true }],
            }));
        Assert.Equal(
            "{\"groups\":[{\"name\":\"CLR\",\"settings\":[{\"name\":\"A\",\"breakWhenThrown\":true}],\"total\":1,\"truncated\":false}]}",
            ProtocolJson.Serialize(new ExceptionSettingsPage
            {
                Groups = [new ExceptionGroupInfo { Name = "CLR", Settings = [new ExceptionSettingInfo { Name = "A", BreakWhenThrown = true }], Total = 1 }],
            }));
        Assert.Equal(
            "{\"restored\":[]}",
            ProtocolJson.Serialize(new ExceptionResetResult()));
    }

    [Fact]
    public void Exception_settings_round_trip()
    {
        var copy = RoundTrip(new ExceptionResetResult
        {
            Restored = [new ExceptionChange { Name = "CLR/A", Previous = true, Current = false, Changed = true }],
        });

        Assert.Equal("CLR/A", copy.Restored[0].Name);
        Assert.True(copy.Restored[0].Previous);
        Assert.False(copy.Restored[0].Current);
    }

    [Fact]
    public void Watch_wire_format()
    {
        Assert.Equal(
            "{\"id\":\"w1\",\"expression\":\"order.Total\",\"name\":\"total\",\"value\":\"12\",\"type\":\"decimal\",\"changed\":true}",
            ProtocolJson.Serialize(new WatchValue { Id = "w1", Expression = "order.Total", Name = "total", Value = "12", Type = "decimal", Changed = true }));
        Assert.Equal(
            "{\"id\":\"w2\",\"expression\":\"x\",\"changed\":false,\"error\":\"not in scope\"}",
            ProtocolJson.Serialize(new WatchValue { Id = "w2", Expression = "x", Error = "not in scope" }));
        Assert.Equal("{\"watches\":[]}", ProtocolJson.Serialize(new WatchListResult()));
        Assert.Equal("{\"removed\":[]}", ProtocolJson.Serialize(new RemoveWatchResult()));
        Assert.Equal(
            "{\"watch\":{\"id\":\"w1\",\"expression\":\"i\",\"changed\":false},\"alreadyExisted\":true}",
            ProtocolJson.Serialize(new AddWatchResult { Watch = new WatchValue { Id = "w1", Expression = "i" }, AlreadyExisted = true }));
    }

    [Fact]
    public void Value_page_wire_format()
    {
        Assert.Equal(
            "{\"value\":\"\\\"abc\",\"type\":\"string\",\"offset\":0,\"totalLength\":5,\"hasMore\":true,\"masked\":true}",
            ProtocolJson.Serialize(new ValuePage { Value = "\"abc", Type = "string", Offset = 0, TotalLength = 5, HasMore = true, Masked = true }));
        Assert.Equal(
            "{\"value\":\"\",\"offset\":9,\"totalLength\":5,\"hasMore\":false}",
            ProtocolJson.Serialize(new ValuePage { Offset = 9, TotalLength = 5 }));
    }

    [Fact]
    public void Watch_round_trip()
    {
        var copy = RoundTrip(new WatchListResult { Watches = [new WatchValue { Id = "w1", Expression = "i", Value = "3", Type = "int", Changed = true }] });

        Assert.Equal("w1", copy.Watches[0].Id);
        Assert.Equal("3", copy.Watches[0].Value);
        Assert.True(copy.Watches[0].Changed);
        Assert.Null(copy.Watches[0].Error);
    }

    [Fact]
    public void A_minor_7_payload_still_deserializes()
    {
        var old = ProtocolJson.Deserialize<SymbolStatus>("{\"justMyCode\":true,\"unbound\":[],\"notes\":[]}")!;

        Assert.Equal(true, old.JustMyCode);
        Assert.Empty(old.Notes);
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
            Pid = 1234, PipeName = "debugviamcp-abc", Token = "t", SolutionPath = @"C:\s\a.sln",
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
    public void InstanceRecord_without_lease_fields_omits_them_and_a_2_1_record_reads_with_them_null()
    {
        var json = ProtocolJson.Serialize(new InstanceRecord { Pid = 1, PipeName = "p", Token = "t" });

        Assert.DoesNotContain("leaseHolder", json, StringComparison.OrdinalIgnoreCase);
        var old = ProtocolJson.Deserialize<InstanceRecord>("{\"pid\":7,\"pipeName\":\"p\",\"token\":\"t\",\"protocolMajor\":2,\"protocolMinor\":1}")!;
        Assert.Equal(7, old.Pid);
        Assert.Null(old.LeaseHolder);
        Assert.Null(old.LeaseHolderFolder);
    }

    [Fact]
    public void InstanceRecord_lease_fields_round_trip()
    {
        var copy = RoundTrip(new InstanceRecord { Pid = 1, LeaseHolder = "other-agent", LeaseHolderFolder = @"D:\x" });

        Assert.Equal("other-agent", copy.LeaseHolder);
        Assert.Equal(@"D:\x", copy.LeaseHolderFolder);
    }

    [Fact]
    public void Run_to_reason_is_camel_case_on_the_wire()
    {
        Assert.Contains("\"reason\":\"runTo\"", ProtocolJson.Serialize(new BreakSnapshot { Reason = BreakReason.RunTo }), StringComparison.Ordinal);
    }
}
