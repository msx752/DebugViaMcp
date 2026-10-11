using VsDebug.Vsix.Core;
using Xunit;

namespace VsDebug.Vsix.Core.Tests;

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

    [Fact]
    public void Source_context_of_missing_file_is_empty()
        => Assert.Empty(PathUtil.SourceContext(@"C:\does\not\exist.cs", 3));
}
