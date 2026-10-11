namespace VsMcpDebugger.Protocol;

/// <summary>JSON-RPC error codes used by the bridge. Values are part of the wire contract.</summary>
public enum BridgeErrorCode
{
    NotConnected = -32001,
    AmbiguousInstance = -32002,
    WrongMode = -32003,
    Busy = -32004,
    VersionMismatch = -32005,
    Unauthorized = -32006,
    InvalidArgument = -32007,
    EvalFailed = -32008,
    NotFound = -32009,
    Denied = -32010,
    AiDisabled = -32011,
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
        _ => "internal",
    };
}
