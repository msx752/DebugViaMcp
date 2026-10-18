using Xunit;
using DebugViaMcp.Vsix.Core.Builds;

namespace DebugViaMcp.Vsix.Core.Tests.Builds;

public class StaleBuildCheckerTests
{
    private static readonly DateTime Noon = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static ProjectBuildInfo Info(DateTime? source = null, DateTime? output = null, bool? upToDate = null) =>
        new() { UniqueName = "App.csproj", NewestSourceUtc = source, OldestOutputUtc = output, UpToDateByVisualStudio = upToDate };

    /// <summary>
    /// Found in the real Visual Studio run: deps.json and runtimeconfig.json are not rewritten by an incremental build, so
    /// counting every file of the Built output group made a freshly built project look stale.
    /// </summary>
    [Fact]
    public void Only_the_assembly_and_the_symbols_count_as_build_products()
    {
        var files = StaleBuildChecker.ProductFiles(
            [@"C:\bin\App.dll", @"C:\bin\App.exe", @"C:\bin\App.deps.json", @"C:\bin\App.runtimeconfig.json"],
            [@"C:\bin\App.pdb"]);

        Assert.Equal([@"C:\bin\App.dll", @"C:\bin\App.pdb"], files);
    }

    [Fact]
    public void A_framework_exe_is_the_assembly_when_there_is_no_dll()
    {
        var files = StaleBuildChecker.ProductFiles([@"C:\bin\App.exe", @"C:\bin\App.exe.config"], [@"C:\bin\App.pdb"]);

        Assert.Equal([@"C:\bin\App.exe", @"C:\bin\App.pdb"], files);
    }

    [Fact]
    public void Extensions_are_compared_without_regard_to_case_and_other_files_are_ignored()
    {
        var files = StaleBuildChecker.ProductFiles([@"C:\bin\APP.DLL", @"C:\bin\readme.txt"], [@"C:\bin\APP.PDB", @"C:\bin\x.json"]);

        Assert.Equal([@"C:\bin\APP.DLL", @"C:\bin\APP.PDB"], files);
    }

    [Fact]
    public void No_assembly_among_the_outputs_gives_no_products() =>
        Assert.Empty(StaleBuildChecker.ProductFiles([@"C:\bin\App.deps.json"], []));

    [Fact]
    public void Fresh_output_is_not_stale() =>
        Assert.False(StaleBuildChecker.IsStale(Info(source: Noon, output: Noon.AddMinutes(1))));

    [Fact]
    public void Output_written_at_the_same_moment_as_the_newest_source_is_not_stale() =>
        Assert.False(StaleBuildChecker.IsStale(Info(source: Noon, output: Noon)));

    [Fact]
    public void A_newer_source_is_stale() =>
        Assert.True(StaleBuildChecker.IsStale(Info(source: Noon.AddMinutes(1), output: Noon)));

    [Fact]
    public void Missing_output_is_stale() =>
        Assert.True(StaleBuildChecker.IsStale(Info(source: Noon, output: null)));

    [Fact]
    public void An_older_pdb_makes_it_stale_even_when_the_dll_is_new()
    {
        // The facade reports the oldest of dll and pdb as OldestOutputUtc.
        Assert.True(StaleBuildChecker.IsStale(Info(source: Noon, output: Noon.AddMinutes(-5))));
    }

    [Fact]
    public void Visual_Studio_saying_stale_wins_over_timestamps() =>
        Assert.True(StaleBuildChecker.IsStale(Info(source: Noon, output: Noon.AddHours(1), upToDate: false)));

    [Fact]
    public void Visual_Studio_saying_up_to_date_wins_over_timestamps() =>
        Assert.False(StaleBuildChecker.IsStale(Info(source: Noon.AddHours(1), output: Noon, upToDate: true)));

    [Fact]
    public void Visual_Studio_saying_up_to_date_wins_even_without_output_times() =>
        Assert.False(StaleBuildChecker.IsStale(Info(source: Noon, output: null, upToDate: true)));

    [Fact]
    public void No_sources_known_and_an_output_present_is_not_stale() =>
        Assert.False(StaleBuildChecker.IsStale(Info(source: null, output: Noon)));
}
