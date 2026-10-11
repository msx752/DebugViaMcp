using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Ui;

/// <summary>View → Output → "VsMcpDebugger Activity": one line per audited operation.</summary>
internal sealed class ActivityPane(IVsOutputWindowPane pane, Action showOutputWindow) : IActivitySink
{
    public static readonly Guid PaneGuid = new("d41f7a2c-6b83-4e95-b0c7-2a9e5f81d3b6");

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
