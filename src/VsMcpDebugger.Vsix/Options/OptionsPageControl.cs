using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace VsMcpDebugger.Vsix.Options;

/// <summary>
/// The page body of Tools → Options → Debugging → Debugging via MCP: a short "AI agent setup" section on top (a button that
/// checks what is missing and, after confirmation, installs and registers the MCP server) and the settings grid below.
/// The layout follows the dialog's size (no fixed positions), so text wraps instead of being cut off.
/// </summary>
internal sealed class OptionsPageControl : UserControl
{
    private const string Explanation =
        "The extension needs the vsmcpdebugger MCP server (a .NET tool) and a registration with your AI agent, for example Claude Code. " +
        "The button checks what is missing and, only after you confirm, runs the commands that install and register it.";

    private readonly Label _explanation;
    private readonly TableLayoutPanel _header;

    public OptionsPageControl(object page)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
        var text = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
        BackColor = background;
        ForeColor = text;

        var title = new Label
        {
            Text = "AI agent setup", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = text, Margin = new Padding(0, 0, 0, 4),
        };
        _explanation = new Label { Text = Explanation, AutoSize = true, ForeColor = text, Margin = new Padding(0, 0, 0, 8) };

        var setup = new Button { Text = "Set Up AI Agent…", AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(0, 0, 8, 0) };
        setup.Click += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsMcpDebuggerPackage.Instance?.RunMcpSetup();
        };
        var copy = new Button { Text = "Copy setup commands", AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(0) };
        copy.Click += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsMcpDebuggerPackage.Instance?.CopyMcpSetupCommands();
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), BackColor = background };
        buttons.Controls.Add(setup);
        buttons.Controls.Add(copy);

        _header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(8, 8, 8, 10), BackColor = background,
        };
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _header.Controls.Add(title, 0, 0);
        _header.Controls.Add(_explanation, 0, 1);
        _header.Controls.Add(buttons, 0, 2);
        _header.SizeChanged += (_, _) => WrapExplanation();

        var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = page, ToolbarVisible = false, HelpVisible = true };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(_header, 0, 0);
        root.Controls.Add(grid, 0, 1);
        Controls.Add(root);
        WrapExplanation();
    }

    /// <summary>A label only wraps when it knows its maximum width; keep that in step with the dialog.</summary>
    private void WrapExplanation()
    {
        var available = Math.Max(160, _header.ClientSize.Width - _header.Padding.Horizontal);
        _explanation.MaximumSize = new Size(available, 0);
    }
}
