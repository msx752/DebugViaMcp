using StreamJsonRpc;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Connection;

/// <summary>Creates JsonRpc with the language-neutral wire format (spec §2.4). Never construct JsonRpc any other way.</summary>
public static class RpcFactory
{
    public static JsonRpc Create(Stream stream)
    {
        var formatter = new JsonMessageFormatter();
        ProtocolJson.Configure(formatter.JsonSerializer);
        return new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream, formatter));
    }
}
