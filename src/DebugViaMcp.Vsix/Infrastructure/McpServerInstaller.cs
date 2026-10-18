using System;
using System.IO;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Setup;

namespace DebugViaMcp.Vsix.Infrastructure;

/// <summary>Puts the MCP server that travels inside this extension where agents can start it (see <see cref="McpServerDeployer"/>).</summary>
internal static class McpServerInstaller
{
    private static string BundleDirectory =>
        Path.Combine(Path.GetDirectoryName(typeof(McpServerInstaller).Assembly.Location) ?? "", "McpServer");

    /// <summary>Never throws: a failed install is logged and shows up in Set Up AI Agent.</summary>
    public static bool TryDeploy(ILog log)
    {
        try
        {
            new McpServerDeployer(BundleDirectory, DiscoveryPaths.Root, log).Deploy();
            return true;
        }
        catch (IOException e)
        {
            log.Error("Could not install the MCP server", e);
            return false;
        }
        catch (UnauthorizedAccessException e)
        {
            log.Error("Could not install the MCP server", e);
            return false;
        }
    }
}
