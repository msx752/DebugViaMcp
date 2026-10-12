using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VsMcpDebugger.Protocol;

/// <summary>
/// Pure path rules that decide which Visual Studio an agent belongs to. Only fully qualified paths are considered;
/// null, empty, relative or malformed input never matches. Folders are compared whole segment by segment, so
/// <c>D:\proj-evil</c> is not inside <c>D:\proj</c>.
/// </summary>
public static class FolderMatch
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary><paramref name="candidate"/> is <paramref name="folder"/> or lies below it.</summary>
    public static bool IsInsideOrEqual(string? candidate, string? folder)
    {
        var c = Normalize(candidate);
        var f = Normalize(folder);
        if (c is null || f is null) return false;

        var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(c, f, comparison)) return true;
        return c.StartsWith(f + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>Directory of the solution file; null when there is no solution path.</summary>
    public static string? SolutionFolder(string? solutionPath)
    {
        var full = Normalize(solutionPath);
        return full is null ? null : Path.GetDirectoryName(full);
    }

    /// <summary>The working directory is at or below the solution folder.</summary>
    public static bool Trusts(string? clientWorkingDirectory, string? solutionPath) =>
        IsInsideOrEqual(clientWorkingDirectory, SolutionFolder(solutionPath));

    /// <summary>The working directory is trusted, or the solution folder lies below it (an agent at the repository root).</summary>
    public static bool Relates(string? clientWorkingDirectory, string? solutionPath)
    {
        var folder = SolutionFolder(solutionPath);
        return IsInsideOrEqual(clientWorkingDirectory, folder) || IsInsideOrEqual(folder, clientWorkingDirectory);
    }

    private static string? Normalize(string? path)
    {
        if (path is null || string.IsNullOrWhiteSpace(path) || !IsFullyQualified(path)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsFullyQualified(string path)
    {
        if (!IsWindows) return path[0] == '/';
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }
}
