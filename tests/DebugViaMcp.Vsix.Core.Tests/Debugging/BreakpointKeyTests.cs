using Xunit;
using DebugViaMcp.Vsix.Core.Debugging;

namespace DebugViaMcp.Vsix.Core.Tests.Debugging;

public class BreakpointKeyTests
{
    [Fact]
    public void Same_location_and_condition_give_same_id_regardless_of_path_case()
        => Assert.Equal(
            BreakpointKey.Compute(@"C:\Src\A.cs", 9, null, "x > 1"),
            BreakpointKey.Compute(@"c:\src\a.cs", 9, null, "x > 1"));

    [Fact]
    public void Different_condition_gives_different_id()
        => Assert.NotEqual(
            BreakpointKey.Compute(@"C:\a.cs", 9, null, "x > 1"),
            BreakpointKey.Compute(@"C:\a.cs", 9, null, "x > 2"));

    [Fact]
    public void Id_has_stable_shape()
        => Assert.Matches("^bp-[0-9a-f]{8}$", BreakpointKey.Compute(null, null, "A.B.C", null));
}
