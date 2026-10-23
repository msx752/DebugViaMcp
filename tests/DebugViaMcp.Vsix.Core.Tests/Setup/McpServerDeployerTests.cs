using Xunit;
using DebugViaMcp.Vsix.Core.Setup;
using DebugViaMcp.Vsix.Core.Tests.Fakes;

namespace DebugViaMcp.Vsix.Core.Tests.Setup;

public sealed class McpServerDeployerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "dvm-deploy-" + Guid.NewGuid().ToString("N"));
    private string Bundle => Path.Combine(_temp, "ext", "McpServer");
    private string Root => Path.Combine(_temp, "data");

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
    }

    private void MakeBundle(string serverText = "server v1")
    {
        Directory.CreateDirectory(Path.Combine(Bundle, "sub"));
        File.WriteAllText(Path.Combine(Bundle, McpServerDeployer.MainAssembly), serverText);
        File.WriteAllText(Path.Combine(Bundle, "sub", "dep.dll"), "dep");
    }

    private McpServerDeployer Deployer() => new(Bundle, Root, new NullLog());

    private string CurrentBuild() => File.ReadAllText(Path.Combine(Root, "bin", "current.txt")).Trim();

    [Fact]
    public void Copies_the_server_writes_the_launcher_and_marks_the_build_complete()
    {
        MakeBundle();

        var launcher = Deployer().Deploy();

        Assert.Equal(Path.Combine(Root, "debugviamcp.cmd"), launcher);
        var build = Path.Combine(Root, "bin", CurrentBuild());
        Assert.True(File.Exists(Path.Combine(build, McpServerDeployer.MainAssembly)));
        Assert.True(File.Exists(Path.Combine(build, "sub", "dep.dll")));
        Assert.True(McpServerDeployer.IsDeployed(Root));
        var text = File.ReadAllText(launcher);
        Assert.Contains("DebugViaMcp.Mcp.dll", text, StringComparison.Ordinal);
        Assert.Contains("%*", text, StringComparison.Ordinal);
        Assert.Contains("NoDefaultCurrentDirectoryInExePath=1", text, StringComparison.Ordinal);   // no dotnet from the agent's folder
    }

    [Fact]
    public void Nothing_is_deployed_before_the_first_deploy()
    {
        MakeBundle();

        Assert.False(McpServerDeployer.IsDeployed(Root));
    }

    [Fact]
    public void A_second_deploy_changes_nothing_and_leaves_the_launcher_untouched()
    {
        MakeBundle();
        var launcher = Deployer().Deploy();
        var build = CurrentBuild();
        var stamp = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(launcher, stamp);

        Deployer().Deploy();

        Assert.Equal(build, CurrentBuild());
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(launcher));
    }

    [Fact]
    public void A_rebuilt_server_gets_its_own_folder_and_the_old_one_goes()
    {
        MakeBundle("server v1");
        Deployer().Deploy();
        var first = CurrentBuild();

        MakeBundle("server v2");
        Deployer().Deploy();

        Assert.NotEqual(first, CurrentBuild());
        Assert.False(Directory.Exists(Path.Combine(Root, "bin", first)));
        Assert.True(McpServerDeployer.IsDeployed(Root));
    }

    [Fact]
    public void An_old_build_that_is_still_in_use_stays_and_does_not_fail_the_deploy()
    {
        MakeBundle("server v1");
        Deployer().Deploy();
        var first = CurrentBuild();
        using var locked = File.Open(Path.Combine(Root, "bin", first, McpServerDeployer.MainAssembly), FileMode.Open, FileAccess.Read, FileShare.Read);

        MakeBundle("server v2");
        Deployer().Deploy();

        Assert.NotEqual(first, CurrentBuild());
        Assert.True(File.Exists(Path.Combine(Root, "bin", first, McpServerDeployer.MainAssembly)));
        Assert.True(McpServerDeployer.IsDeployed(Root));
    }

    [Fact]
    public void A_half_copied_build_is_copied_again()
    {
        MakeBundle();
        Deployer().Deploy();
        var build = Path.Combine(Root, "bin", CurrentBuild());
        File.Delete(Path.Combine(build, McpServerDeployer.MainAssembly));
        File.Delete(Path.Combine(build, ".complete"));
        Assert.False(McpServerDeployer.IsDeployed(Root));

        Deployer().Deploy();

        Assert.True(File.Exists(Path.Combine(build, McpServerDeployer.MainAssembly)));
        Assert.True(McpServerDeployer.IsDeployed(Root));
    }

    [Fact]
    public void A_bundle_without_the_server_fails_clearly()
    {
        Directory.CreateDirectory(Bundle);

        Assert.Throws<FileNotFoundException>(() => Deployer().Deploy());
    }

    [Fact]
    public void The_launcher_text_never_changes_so_running_agents_can_keep_reading_it()
    {
        MakeBundle("server v1");
        var launcher = Deployer().Deploy();
        var before = File.ReadAllText(launcher);

        MakeBundle("server v2");
        Deployer().Deploy();

        Assert.Equal(before, File.ReadAllText(launcher));
    }

    [Fact]
    public void A_pointer_file_that_a_launcher_is_reading_never_fails_the_deploy()
    {
        MakeBundle("server v1");
        Deployer().Deploy();
        var first = CurrentBuild();
        using var reading = File.Open(Path.Combine(Root, "bin", "current.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        MakeBundle("server v2");
        var launcher = Deployer().Deploy();   // must not throw: the user is not to see an "in use" error

        Assert.Equal(Path.Combine(Root, "debugviamcp.cmd"), launcher);
        reading.Dispose();
        Assert.Equal(first, CurrentBuild());   // the old server keeps working until the next start points at the new one
        Deployer().Deploy();
        Assert.NotEqual(first, CurrentBuild());
    }

    [Fact]
    public void Deploying_twice_in_a_row_allocates_only_a_small_buffer_for_hashing()
    {
        MakeBundle();
        Deployer().Deploy();
        var big = new string('x', 6 * 1024 * 1024);
        File.WriteAllText(Path.Combine(Bundle, McpServerDeployer.MainAssembly), big);   // a large assembly, as the real one

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = McpServerDeployer.BuildName(Bundle);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024, $"hashing a 6 MB assembly allocated {allocated} bytes");
    }
}
