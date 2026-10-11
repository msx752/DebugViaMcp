using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class AiAccessSwitchTests
{
    [Fact]
    public void Attach_is_refused_while_disconnected()
    {
        var access = new AiAccessSwitch(enabled: false);
        Assert.Null(access.TryAttach("claude-code", () => { }));
        Assert.Empty(access.Clients);
    }

    [Fact]
    public void Disconnect_drops_every_connection_and_raises_changed()
    {
        var access = new AiAccessSwitch(enabled: true);
        var dropped = 0;
        var changed = 0;
        access.Changed += (_, _) => changed++;
        access.TryAttach("a", () => dropped++);
        access.TryAttach("b", () => dropped++);
        Assert.Equal(["a", "b"], access.Clients.OrderBy(c => c, StringComparer.Ordinal));

        access.Disconnect();

        Assert.Equal(2, dropped);
        Assert.False(access.Enabled);
        Assert.Empty(access.Clients);
        Assert.Equal(3, changed);
    }

    [Fact]
    public void Reconnect_allows_new_connections()
    {
        var access = new AiAccessSwitch(enabled: true);
        access.Disconnect();
        access.Reconnect();
        Assert.True(access.Enabled);
        Assert.NotNull(access.TryAttach("a", () => { }));
    }

    [Fact]
    public void Disposing_a_registration_removes_the_client()
    {
        var access = new AiAccessSwitch(enabled: true);
        var registration = access.TryAttach("a", () => { });
        registration!.Dispose();
        Assert.Empty(access.Clients);
    }
}
