using System;

namespace VsMcpDebugger.Vsix.Core;

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
    /// <summary>Visual Studio's own answer wins when present; otherwise stale when no output exists or a source is newer than the oldest output.</summary>
    public static bool IsStale(ProjectBuildInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        if (info.UpToDateByVisualStudio is { } upToDate) return !upToDate;
        if (info.OldestOutputUtc is not { } output) return true;
        return info.NewestSourceUtc is { } source && source > output;
    }
}
