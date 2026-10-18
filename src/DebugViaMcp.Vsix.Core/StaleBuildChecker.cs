using System;
using System.Collections.Generic;
using System.Linq;

namespace DebugViaMcp.Vsix.Core;

/// <summary>What the facade learned about one project's build state.</summary>
public sealed class ProjectBuildInfo
{
    public string UniqueName { get; set; } = "";

    /// <summary>Newest write time among the project's source files; null when none are known.</summary>
    public DateTime? NewestSourceUtc { get; set; }

    /// <summary>Write time of the oldest build output (assembly and symbols); null when an output is missing.</summary>
    public DateTime? OldestOutputUtc { get; set; }

    /// <summary>Visual Studio's own answer to "is this project up to date"; null when it could not be asked.</summary>
    public bool? UpToDateByVisualStudio { get; set; }
}

public static class StaleBuildChecker
{
    /// <summary>
    /// The files whose write times say whether a build is current: the assembly (the .dll, or the .exe when there is no
    /// .dll) and the symbols. Other files of the output group (deps.json, runtimeconfig.json, apphost) are not rewritten
    /// by an incremental build, so they must not be compared with the sources.
    /// </summary>
    public static IReadOnlyList<string> ProductFiles(IEnumerable<string> built, IEnumerable<string> symbols)
    {
        if (built is null) throw new ArgumentNullException(nameof(built));
        if (symbols is null) throw new ArgumentNullException(nameof(symbols));
        var builtList = built.ToList();
        var assembly = builtList.FirstOrDefault(p => HasExtension(p, ".dll")) ?? builtList.FirstOrDefault(p => HasExtension(p, ".exe"));
        if (assembly is null) return [];
        var result = new List<string> { assembly };
        result.AddRange(symbols.Where(p => HasExtension(p, ".pdb")));
        return result;
    }

    private static bool HasExtension(string path, string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Visual Studio's own answer wins when present; otherwise stale when no output exists or a source is newer than the oldest output.</summary>
    public static bool IsStale(ProjectBuildInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        if (info.UpToDateByVisualStudio is { } upToDate) return !upToDate;
        if (info.OldestOutputUtc is not { } output) return true;
        return info.NewestSourceUtc is { } source && source > output;
    }
}
