using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix.Ui;

/// <summary>View → Output → "Debugging via MCP Activity": one line per audited operation.</summary>
internal sealed class ActivityPane(IVsOutputWindowPane pane, Action showOutputWindow) : IActivitySink
{
    public static readonly Guid PaneGuid = OutputPaneIds.Activity;

    public void Write(string line)
    {
#pragma warning disable VSTHRD010 // OutputStringThreadSafe is documented as callable from any thread.
        pane.OutputStringThreadSafe(line + Environment.NewLine);
#pragma warning restore VSTHRD010
    }

    public void Show()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        showOutputWindow();
        pane.Activate();
    }
}
