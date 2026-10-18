using System;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Core;

/// <summary>A failure with a stable wire error code and a next-step hint; turned into a JSON-RPC error by the connection handler.</summary>
public sealed class BridgeException(BridgeErrorCode code, string message, string? hint = null, string? reason = null) : Exception(message)
{
    public BridgeErrorCode Code { get; } = code;
    public string? Hint { get; } = hint;

    /// <summary>For <see cref="BridgeErrorCode.Denied"/>: one of <see cref="DeniedReasons"/>.</summary>
    public string? Reason { get; } = reason;
}
