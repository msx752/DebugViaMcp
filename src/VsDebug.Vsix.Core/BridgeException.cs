using System;
using VsDebug.Protocol;

namespace VsDebug.Vsix.Core;

/// <summary>A failure with a stable wire error code and a next-step hint; turned into a JSON-RPC error by the connection handler.</summary>
public sealed class BridgeException(BridgeErrorCode code, string message, string? hint = null) : Exception(message)
{
    public BridgeErrorCode Code { get; } = code;
    public string? Hint { get; } = hint;
}
