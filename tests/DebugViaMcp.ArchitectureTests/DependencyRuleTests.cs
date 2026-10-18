using System.Reflection;
using Xunit;

namespace DebugViaMcp.ArchitectureTests;

/// <summary>Fitness function for the rule Vsix → Vsix.Core → Protocol ← Mcp (spec §2.1, §2.4).</summary>
public class DependencyRuleTests
{
    private static readonly Assembly Protocol = typeof(DebugViaMcp.Protocol.IDebugBridge).Assembly;
    private static readonly Assembly Mcp = typeof(DebugViaMcp.Mcp.Session.SessionState).Assembly;
    private static readonly Assembly VsixCore = typeof(DebugViaMcp.Vsix.Core.DebugBridgeService).Assembly;

    private static string[] References(Assembly a) => a.GetReferencedAssemblies().Select(n => n.Name!).ToArray();

    [Fact]
    public void Protocol_references_only_the_base_library_and_newtonsoft()
        => Assert.All(References(Protocol), name => Assert.True(name is "netstandard" or "mscorlib" or "Newtonsoft.Json", $"Protocol references {name}"));

    [Fact]
    public void Mcp_does_not_reference_visual_studio_or_vsix()
        => Assert.DoesNotContain(References(Mcp), n =>
            n.StartsWith("EnvDTE", StringComparison.Ordinal) ||
            n.StartsWith("Microsoft.VisualStudio.Shell", StringComparison.Ordinal) ||
            n.StartsWith("DebugViaMcp.Vsix", StringComparison.Ordinal));

    [Fact]
    public void VsixCore_does_not_reference_mcp_or_visual_studio_shell()
        => Assert.DoesNotContain(References(VsixCore), n =>
            n.StartsWith("ModelContextProtocol", StringComparison.Ordinal) ||
            n.StartsWith("EnvDTE", StringComparison.Ordinal) ||
            n.StartsWith("Microsoft.VisualStudio.Shell", StringComparison.Ordinal) ||
            n.StartsWith("DebugViaMcp.Mcp", StringComparison.Ordinal));

    [Fact]
    public void Mcp_and_protocol_do_not_use_windows_only_assemblies()
        => Assert.DoesNotContain(References(Mcp).Concat(References(Protocol)), n =>
            n.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal) ||
            n is "System.IO.Pipes.AccessControl" or "System.Security.AccessControl");
}
