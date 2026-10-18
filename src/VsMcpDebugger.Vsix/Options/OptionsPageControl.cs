using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace VsMcpDebugger.Vsix.Options;

/// <summary>
/// The page body of Tools → Options → Debugging → Debugging via MCP: a short "AI agent setup" section on top (a button that
/// checks what is missing and, after confirmation, installs and registers the MCP server) and the settings grid below.
/// </summary>
internal sealed class OptionsPageControl : UserControl
{
    public OptionsPageControl(object page)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
        var text = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
        BackColor = background;
        ForeColor = text;

        var title = new Label { Text = "AI agent setup", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Location = new Point(8, 8), ForeColor = text };
        var explanation = new Label
        {
            Text = "The extension needs the vsmcpdebugger MCP server (a .NET tool) and a registration with your AI agent, for example Claude Code. " +
                   "The button checks what is missing and, only after you confirm, runs the commands that install and register it.",
            Location = new Point(8, 30), Size = new Size(560, 48), ForeColor = text,
        };
        var setup = new Button { Text = "Set Up AI Agent…", AutoSize = true, Location = new Point(8, 82), FlatStyle = FlatStyle.System };
        setup.Click += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsMcpDebuggerPackage.Instance?.RunMcpSetup();
        };
        var copy = new Button { Text = "Copy setup commands", AutoSize = true, Location = new Point(150, 82), FlatStyle = FlatStyle.System };
        copy.Click += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsMcpDebuggerPackage.Instance?.CopyMcpSetupCommands();
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = background };
        header.Controls.AddRange([title, explanation, setup, copy]);

        var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = page, ToolbarVisible = false, HelpVisible = true };
        Controls.Add(grid);
        Controls.Add(header);
    }
}
