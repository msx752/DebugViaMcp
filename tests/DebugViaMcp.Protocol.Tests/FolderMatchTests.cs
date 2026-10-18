using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Protocol.Tests;

public class FolderMatchTests
{
    [Fact]
    public void Equal_folder_and_children_are_inside()
    {
        Assert.True(FolderMatch.IsInsideOrEqual(@"D:\proj", @"D:\proj"));
        Assert.True(FolderMatch.IsInsideOrEqual(@"D:\proj\src", @"D:\proj"));
        Assert.True(FolderMatch.IsInsideOrEqual(@"D:\proj\src\a\b", @"D:\proj"));
    }

    [Fact]
    public void Sibling_with_the_same_prefix_is_not_inside()
    {
        Assert.False(FolderMatch.IsInsideOrEqual(@"D:\proj-evil", @"D:\proj"));
        Assert.False(FolderMatch.IsInsideOrEqual(@"D:\project", @"D:\proj"));
        Assert.False(FolderMatch.IsInsideOrEqual(@"D:\proj-evil\src", @"D:\proj"));
    }

    [Fact]
    public void Comparison_ignores_case_and_trailing_separators_on_windows()
    {
        Assert.True(FolderMatch.IsInsideOrEqual(@"d:\PROJ\Src\", @"D:\proj"));
        Assert.True(FolderMatch.IsInsideOrEqual(@"D:\proj", @"D:\proj\"));
        Assert.True(FolderMatch.IsInsideOrEqual(@"D:\proj\\src", @"D:\proj"));
    }

    [Fact]
    public void Dot_dot_segments_are_resolved_before_comparing()
    {
        Assert.False(FolderMatch.IsInsideOrEqual(@"D:\proj\x\..\..\other", @"D:\proj"));
        Assert.True(FolderMatch.IsInsideOrEqual(@"D:\proj\x\..", @"D:\proj"));
        Assert.False(FolderMatch.IsInsideOrEqual(@"D:\proj\..", @"D:\proj"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"proj\src")]
    [InlineData(".")]
    public void Null_empty_and_relative_paths_never_match(string? path)
    {
        Assert.False(FolderMatch.IsInsideOrEqual(path, @"D:\proj"));
        Assert.False(FolderMatch.IsInsideOrEqual(@"D:\proj", path));
        Assert.False(FolderMatch.Trusts(path, @"D:\proj\App.sln"));
        Assert.False(FolderMatch.Trusts(@"D:\proj", path));
        Assert.False(FolderMatch.Relates(path, @"D:\proj\App.sln"));
        Assert.False(FolderMatch.Relates(@"D:\proj", path));
    }

    [Fact]
    public void Trusts_needs_the_working_directory_at_or_below_the_solution_folder()
    {
        const string solution = @"D:\proj\src\App.sln";

        Assert.True(FolderMatch.Trusts(@"D:\proj\src", solution));
        Assert.True(FolderMatch.Trusts(@"D:\proj\src\x", solution));
        Assert.False(FolderMatch.Trusts(@"D:\proj", solution));
        Assert.False(FolderMatch.Trusts(@"D:\proj\src-evil", solution));
    }

    [Fact]
    public void Relates_also_accepts_a_solution_below_the_working_directory()
    {
        const string solution = @"D:\proj\src\App.sln";

        Assert.True(FolderMatch.Relates(@"D:\proj", solution));
        Assert.False(FolderMatch.Trusts(@"D:\proj", solution));
        Assert.True(FolderMatch.Relates(@"D:\proj\src\x", solution));
        Assert.False(FolderMatch.Relates(@"D:\other", solution));
        Assert.False(FolderMatch.Trusts(@"D:\other", solution));
    }

    [Fact]
    public void Solution_path_null_means_no_solution_and_matches_nothing()
    {
        Assert.Null(FolderMatch.SolutionFolder(null));
        Assert.Null(FolderMatch.SolutionFolder(""));
        Assert.False(FolderMatch.Trusts(@"D:\proj", null));
        Assert.False(FolderMatch.Relates(@"D:\proj", null));
    }

    [Fact]
    public void Solution_folder_is_the_directory_of_the_solution_file()
    {
        Assert.Equal(@"D:\proj\src", FolderMatch.SolutionFolder(@"D:\proj\src\App.sln"));
    }

    [Fact]
    public void UNC_paths_compare_like_drive_paths()
    {
        Assert.True(FolderMatch.IsInsideOrEqual(@"\\server\share\proj\src", @"\\server\share\proj"));
        Assert.False(FolderMatch.IsInsideOrEqual(@"\\server\share\proj-evil", @"\\server\share\proj"));
        Assert.True(FolderMatch.Trusts(@"\\server\share\proj", @"\\server\share\proj\App.sln"));
    }
}
