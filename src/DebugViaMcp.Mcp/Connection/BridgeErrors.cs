using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Connection;

/// <summary>Turns a JSON-RPC error from the IDE into code, hint and reason.</summary>
public static class BridgeErrors
{
    public static (BridgeErrorCode Code, string? Hint, string? Reason) Read(RemoteInvocationException e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var code = Enum.IsDefined(typeof(BridgeErrorCode), e.ErrorCode) ? (BridgeErrorCode)e.ErrorCode : BridgeErrorCode.Internal;
        var data = e.ErrorData switch
        {
            JObject o => o.ToObject<BridgeErrorData>(JsonSerializer.Create(ProtocolJson.Settings)),
            JValue { Type: JTokenType.String } s => new BridgeErrorData { Hint = s.Value<string>() },
            string s => new BridgeErrorData { Hint = s },
            _ => null,
        };
        return (code, data?.Hint, data?.Reason);
    }
}
