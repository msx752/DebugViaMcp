using System;
using System.IO;

namespace VsMcpDebugger.Protocol;

/// <summary>Shared by the extension and the MCP server: both delete their own old log files by the same Options setting.</summary>
public static class LogRetention
{
    /// <summary>Deletes <c>{prefix}*.log</c> files not written for more than <paramref name="days"/> days. Never throws for I/O problems.</summary>
    public static void Prune(string directory, string prefix, int days, DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            var cutoff = now.UtcDateTime.AddDays(-Math.Max(1, days));
            foreach (var file in Directory.GetFiles(directory, prefix + "*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
