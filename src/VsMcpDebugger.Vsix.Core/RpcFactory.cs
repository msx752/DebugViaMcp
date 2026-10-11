using System;
using System.IO;
using StreamJsonRpc;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Creates JsonRpc with the language-neutral wire format (spec §2.4). Deliberately duplicates the Mcp
/// RpcFactory: the dependency rule forbids sharing StreamJsonRpc code through Protocol. The golden test in
/// Protocol and PipeHostTests keep the two in sync.
/// </summary>
public static class RpcFactory
{
    public static JsonRpc Create(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var formatter = new JsonMessageFormatter();
        ProtocolJson.Configure(formatter.JsonSerializer);
        return new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream, formatter));
    }
}
