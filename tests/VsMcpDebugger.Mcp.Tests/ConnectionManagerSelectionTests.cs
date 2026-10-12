using Microsoft.Extensions.Logging.Abstractions;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Mcp.Tests;

public class ConnectionManagerSelectionTests
{
    private static InstanceRecord Rec(int pid, string? solution = null, string? holder = null) =>
        new() { Pid = pid, PipeName = "p" + pid, Token = "t", SolutionPath = solution, LeaseHolder = holder };

    private sealed class Attempts
    {
        public List<int> Pids { get; } = [];

        public Task<BridgeConnection> Connect(InstanceRecord r, SessionState s, CancellationToken ct)
        {
            Pids.Add(r.Pid);
            throw new McpBridgeException(BridgeErrorCode.NotConnected, "stop here", null);
        }
    }

    private static async Task<(int? Pid, McpBridgeException? Error)> Choose(IReadOnlyList<InstanceRecord> instances, string workingDirectory)
    {
        var attempts = new Attempts();
        await using var manager = new ConnectionManager(() => instances, attempts.Connect, new SessionState(), NullLogger.Instance, workingDirectory);
        try
        {
            await manager.GetBridgeAsync(CancellationToken.None);
            return (null, null);
        }
        catch (McpBridgeException e) when (e.Code == BridgeErrorCode.AmbiguousInstance)
        {
            return (null, e);
        }
        catch (McpBridgeException)
        {
            return (attempts.Pids.SingleOrDefault(), null);
        }
    }

    [Fact]
    public async Task Single_instance_is_chosen_whatever_the_folder()
    {
        var (pid, error) = await Choose([Rec(1, @"D:\a\A.sln")], @"D:\unrelated");

        Assert.Null(error);
        Assert.Equal(1, pid);
    }

    [Fact]
    public async Task Several_instances_choose_the_only_one_related_to_the_folder()
    {
        var (pid, error) = await Choose([Rec(1, @"D:\a\A.sln"), Rec(2, @"D:\b\B.sln")], @"D:\b\src");

        Assert.Null(error);
        Assert.Equal(2, pid);
    }

    [Fact]
    public async Task Solution_below_the_working_directory_selects_when_it_is_the_only_one()
    {
        var (pid, error) = await Choose([Rec(1, @"D:\a\A.sln"), Rec(2, @"D:\b\src\B.sln")], @"D:\b");

        Assert.Null(error);
        Assert.Equal(2, pid);
    }

    [Fact]
    public async Task Several_related_instances_are_ambiguous_and_the_error_lists_each_with_availability()
    {
        var (pid, error) = await Choose([Rec(11, @"D:\b\src\B.sln"), Rec(22, @"D:\b\tools\C.sln", holder: "claude-code")], @"D:\b");

        Assert.Null(pid);
        Assert.NotNull(error);
        Assert.Contains("11", error.Message, StringComparison.Ordinal);
        Assert.Contains("B.sln", error.Message, StringComparison.Ordinal);
        Assert.Contains("free", error.Message, StringComparison.Ordinal);
        Assert.Contains("22", error.Message, StringComparison.Ordinal);
        Assert.Contains("C.sln", error.Message, StringComparison.Ordinal);
        Assert.Contains("in use by claude-code", error.Message, StringComparison.Ordinal);
        Assert.Contains("vs_select", error.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_related_instance_among_several_is_ambiguous()
    {
        var (pid, error) = await Choose([Rec(1, @"D:\a\A.sln"), Rec(2, @"D:\b\B.sln")], @"D:\elsewhere");

        Assert.Null(pid);
        Assert.Equal(BridgeErrorCode.AmbiguousInstance, error!.Code);
    }

    [Fact]
    public async Task Explicit_select_wins_over_the_folder_rule()
    {
        var attempts = new Attempts();
        await using var manager = new ConnectionManager(() => [Rec(1, @"D:\a\A.sln"), Rec(2, @"D:\b\B.sln")], attempts.Connect, new SessionState(), NullLogger.Instance, @"D:\a");

        await Assert.ThrowsAsync<McpBridgeException>(() => manager.SelectAsync(2, CancellationToken.None));

        Assert.Equal([2], attempts.Pids);
    }

    [Fact]
    public async Task The_working_directory_defaults_to_the_process_directory()
    {
        await using var manager = new ConnectionManager(() => [], new Attempts().Connect, new SessionState(), NullLogger.Instance);

        Assert.Equal(Environment.CurrentDirectory, manager.WorkingDirectory);
    }
}
