using System;

namespace DebugViaMcp.Vsix.Core;

/// <summary>Output window panes by GUID: the extension's own (never readable by the agent) and Visual Studio's well-known ones.</summary>
public static class OutputPaneIds
{
    /// <summary>"Debugging via MCP" log pane.</summary>
    public static readonly Guid Log = new("b5e0c9a4-2f17-4d38-8c6e-91a3d7f02b64");

    /// <summary>"Debugging via MCP Activity" pane: the audit trail shown to the user.</summary>
    public static readonly Guid Activity = new("d41f7a2c-6b83-4e95-b0c7-2a9e5f81d3b6");

    /// <summary>VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid.</summary>
    public static readonly Guid Build = new("1BD8A850-02D1-11D1-BEE7-00A0C913D1F8");

    /// <summary>VSConstants.OutputWindowPaneGuid.DebugPane_guid.</summary>
    public static readonly Guid Debug = new("FC076020-078A-11D1-A7DF-00A0C9110051");

    public static bool IsProtected(Guid id) => id == Log || id == Activity;
}
