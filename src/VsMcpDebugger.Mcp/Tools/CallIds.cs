using System.Globalization;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tools;

/// <summary>Short, per-process call ids (c-000042) that appear in Mcp, VSIX and audit logs.</summary>
internal static class CallIds
{
    private static long _next;

    public static CallContext Next() =>
        new() { CallId = "c-" + Interlocked.Increment(ref _next).ToString("D6", CultureInfo.InvariantCulture) };
}
