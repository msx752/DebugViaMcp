using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>DTE breakpoints have no stable id, so the id is derived from what identifies a breakpoint to the user.</summary>
public static class BreakpointKey
{
    public static string Compute(string? file, int? line, string? function, string? condition)
    {
        var key = string.Join("|",
            (file ?? "").ToUpperInvariant(),
            line?.ToString(CultureInfo.InvariantCulture) ?? "",
            function ?? "",
            condition ?? "");
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var sb = new StringBuilder("bp-", 11);
        for (var i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
