namespace DebugViaMcp.Mcp.Connection;

/// <summary>The MCP client's name (e.g. claude-code) from its initialize request; shown in Visual Studio consent questions.</summary>
public sealed class ClientIdentity
{
    private string? _name;

    public string Name => Volatile.Read(ref _name) ?? "debugviamcp";

    public void Capture(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) Interlocked.CompareExchange(ref _name, name.Trim(), null);
    }
}
