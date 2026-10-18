using Xunit;
using DebugViaMcp.Vsix.Core.Support;

namespace DebugViaMcp.Vsix.Core.Tests.Support;

public class PathUtilTests
{
    [Fact]
    public void Path_under_solution_becomes_relative_with_forward_slashes()
        => Assert.Equal("src/OrderService.cs", PathUtil.ToRelative(@"C:\repo", @"C:\repo\src\OrderService.cs"));

    [Fact]
    public void Path_outside_solution_stays_absolute()
        => Assert.Equal(@"D:\other\X.cs", PathUtil.ToRelative(@"C:\repo", @"D:\other\X.cs"));

    [Fact]
    public void Relative_input_resolves_against_solution()
        => Assert.Equal(@"C:\repo\src\A.cs", PathUtil.ToAbsolute(@"C:\repo", "src/A.cs"));

    [Fact]
    public void Source_context_marks_current_line()
    {
        var file = Path.GetTempFileName();
        File.WriteAllLines(file, ["l1", "l2", "l3", "l4", "l5", "l6", "l7"]);
        try
        {
            var ctx = PathUtil.SourceContext(file, 4, radius: 1);
            Assert.Equal(["3: l3", "> 4: l4", "5: l5"], ctx);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(@"C:\repo\src\A.cs", true)]
    [InlineData(@"C:\REPO\src\A.cs", true)]
    [InlineData(@"C:\repo-other\A.cs", false)]
    [InlineData(@"C:\repo\src\A.cs:hidden", false)]
    [InlineData(@"C:\repo\src\A.cs::$DATA", false)]
    [InlineData(@"\\?\C:\repo\src\A.cs", false)]
    [InlineData(@"\\.\C:\repo\src\A.cs", false)]
    [InlineData(@"\\server\share\repo\A.cs", false)]
    [InlineData(@"C:\repo\src\*.cs", false)]
    public void IsUnder_accepts_only_plain_paths_inside_the_directory(string path, bool expected)
        => Assert.Equal(expected, PathUtil.IsUnder(@"C:\repo", path));

    /// <summary>Final review #5: a minified or generated line must not flood the snapshot.</summary>
    [Fact]
    public void Source_context_truncates_long_lines()
    {
        var file = Path.GetTempFileName();
        File.WriteAllLines(file, ["short", new string('m', 50_000), "short"]);
        try
        {
            var ctx = PathUtil.SourceContext(file, 2, radius: 1);
            Assert.All(ctx, line => Assert.True(line.Length <= PathUtil.MaxSourceLineLength + 8, $"{line.Length} chars"));
            Assert.EndsWith("…", ctx[1], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Source_context_of_missing_file_is_empty()
        => Assert.Empty(PathUtil.SourceContext(@"C:\does\not\exist.cs", 3));
}
