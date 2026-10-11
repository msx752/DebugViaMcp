using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VsDebug.Vsix.Core;

public static class PathUtil
{
    public static string ToRelative(string? solutionDir, string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (string.IsNullOrEmpty(solutionDir)) return path;
        var root = solutionDir!.TrimEnd('\\', '/') + "\\";
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(root.Length).Replace('\\', '/')
            : path;
    }

    /// <summary>Resolves against the solution directory and normalizes ".." segments.</summary>
    public static string ToAbsolute(string? solutionDir, string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        var native = path.Replace('/', '\\');
        if (Path.IsPathRooted(native) || string.IsNullOrEmpty(solutionDir)) return Path.GetFullPath(native);
        return Path.GetFullPath(Path.Combine(solutionDir!, native));
    }

    /// <summary>
    /// True when <paramref name="absolutePath"/> (already normalized) is a plain drive path inside
    /// <paramref name="directory"/>. Device (\\?\, \\.\) and UNC prefixes, alternate data streams
    /// ("file.cs:stream") and wildcards are rejected so the prefix comparison cannot be sidestepped.
    /// </summary>
    public static bool IsUnder(string? directory, string absolutePath)
    {
        if (string.IsNullOrEmpty(directory) || absolutePath is null) return false;
        if (absolutePath.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (absolutePath.IndexOf(':', 2) >= 0 || absolutePath.IndexOfAny(['*', '?']) >= 0) return false;
        var root = Path.GetFullPath(directory!).TrimEnd('\\', '/') + "\\";
        return absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The {expression} placeholders of a tracepoint message.</summary>
    public static IEnumerable<string> Placeholders(string message)
    {
        if (message is null) yield break;
        var start = -1;
        for (var i = 0; i < message.Length; i++)
        {
            if (message[i] == '{')
            {
                start = i + 1;
            }
            else if (message[i] == '}' && start >= 0)
            {
                yield return message.Substring(start, i - start);
                start = -1;
            }
        }
    }

    public static string[] SourceContext(string file, int line, int radius = 3)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }

        var result = new List<string>();
        for (var n = Math.Max(1, line - radius); n <= Math.Min(lines.Length, line + radius); n++)
        {
            result.Add((n == line ? "> " : "") + n.ToString(CultureInfo.InvariantCulture) + ": " + lines[n - 1]);
        }

        return result.ToArray();
    }
}
