namespace VsMcpDebugger.Protocol;

/// <summary>
/// JSON-RPC error codes used by the bridge. Values are part of the wire contract.
/// StreamJsonRpc reserves -32000..-32003 (invocation error, no marshaled object, response serialization failure,
/// invocation error with exception): a code in that range is not delivered as a coded error. NotConnected and
/// AmbiguousInstance are raised by the MCP server itself and never sent over the pipe, so their values do no harm;
/// every code the extension sends must stay outside the reserved range (pinned by
/// BridgeConnectionTests.Every_code_the_extension_sends_reaches_the_server_as_a_coded_error).
/// </summary>
public enum BridgeErrorCode
{
    NotConnected = -32001,
    AmbiguousInstance = -32002,

    /// <summary>Was -32003 in 0.2, which StreamJsonRpc reserves; changed in 0.3 (additive: a 0.2 server maps the unknown value to internal).</summary>
    WrongMode = -32014,
    Busy = -32004,
    VersionMismatch = -32005,
    Unauthorized = -32006,
    InvalidArgument = -32007,
    EvalFailed = -32008,
    NotFound = -32009,
    Denied = -32010,
    AiDisabled = -32011,
    Stale = -32012,
    Unsupported = -32013,
    Internal = -32099,
}

public static class BridgeErrorNames
{
    public static string ToName(BridgeErrorCode code) => code switch
    {
        BridgeErrorCode.NotConnected => "not_connected",
        BridgeErrorCode.AmbiguousInstance => "ambiguous_instance",
        BridgeErrorCode.WrongMode => "wrong_mode",
        BridgeErrorCode.Busy => "busy",
        BridgeErrorCode.VersionMismatch => "version_mismatch",
        BridgeErrorCode.Unauthorized => "unauthorized",
        BridgeErrorCode.InvalidArgument => "invalid_argument",
        BridgeErrorCode.EvalFailed => "eval_failed",
        BridgeErrorCode.NotFound => "not_found",
        BridgeErrorCode.Denied => "denied",
        BridgeErrorCode.AiDisabled => "ai_disabled",
        BridgeErrorCode.Stale => "stale",
        BridgeErrorCode.Unsupported => "unsupported",
        _ => "internal",
    };
}
