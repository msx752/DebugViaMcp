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

    public static string ToAbsolute(string? solutionDir, string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (Path.IsPathRooted(path) || string.IsNullOrEmpty(solutionDir)) return path;
        return Path.GetFullPath(Path.Combine(solutionDir!, path.Replace('/', '\\')));
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
