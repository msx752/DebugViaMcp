using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace VsMcpDebugger.Vsix.Options;

/// <summary>
/// The page body of Tools → Options → Debugging → Debugging via MCP: a short "AI agent setup" section on top (a button that
/// checks what is missing and, after confirmation, installs and registers the MCP server) and the settings grid below.
/// The layout follows the dialog's size (no fixed positions), so text wraps instead of being cut off, and the colors follow
/// the Visual Studio theme (also when the theme changes while the dialog is open).
/// </summary>
internal sealed class OptionsPageControl : UserControl
{
    private const string Explanation =
        "The extension needs the vsmcpdebugger MCP server (a .NET tool) and a registration with your AI agent, for example Claude Code. " +
        "The button checks what is missing and, only after you confirm, runs the commands that install and register it.";

    private readonly Label _title;
    private readonly Label _explanation;
    private readonly TableLayoutPanel _header;
    private readonly FlowLayoutPanel _buttons;
    private readonly TableLayoutPanel _root;
    private readonly PropertyGrid _grid;

    public OptionsPageControl(object page)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _title = new Label { Text = "AI agent setup", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        _explanation = new Label { Text = Explanation, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };

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
        _buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        _buttons.Controls.Add(setup);
        _buttons.Controls.Add(copy);

        _header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(8, 8, 8, 10) };
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _header.Controls.Add(_title, 0, 0);
        _header.Controls.Add(_explanation, 0, 1);
        _header.Controls.Add(_buttons, 0, 2);
        _header.SizeChanged += (_, _) => WrapExplanation();

        _grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = page, ToolbarVisible = false, HelpVisible = true };

        _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.Controls.Add(_header, 0, 0);
        _root.Controls.Add(_grid, 0, 1);
        Controls.Add(_root);

        ApplyTheme();
        WrapExplanation();
        VSColorTheme.ThemeChanged += OnThemeChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) VSColorTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    private void OnThemeChanged(ThemeChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ApplyTheme();
    }

    /// <summary>Colors come from the current Visual Studio theme; the grid's own colors are derived from them.</summary>
    private void ApplyTheme()
    {
        var back = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
        var text = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
        var line = Blend(back, text, 0.18);
        var dim = Blend(text, back, 0.45);

        BackColor = back;
        ForeColor = text;
        foreach (var panel in new Control[] { _root, _header, _buttons }) panel.BackColor = back;
        _title.ForeColor = text;
        _explanation.ForeColor = text;

        _grid.BackColor = back;
        _grid.ViewBackColor = back;
        _grid.ViewForeColor = text;
        _grid.LineColor = line;
        _grid.ViewBorderColor = line;
        _grid.CategoryForeColor = text;
        _grid.CategorySplitterColor = line;
        _grid.HelpBackColor = back;
        _grid.HelpForeColor = text;
        _grid.HelpBorderColor = line;
        _grid.DisabledItemForeColor = dim;
        _grid.SelectedItemWithFocusBackColor = VSColorTheme.GetThemedColor(EnvironmentColors.SystemHighlightColorKey);
        _grid.SelectedItemWithFocusForeColor = VSColorTheme.GetThemedColor(EnvironmentColors.SystemHighlightTextColorKey);
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>A label only wraps when it knows its maximum width; keep that in step with the dialog.</summary>
    private void WrapExplanation()
    {
        var available = Math.Max(160, _header.ClientSize.Width - _header.Padding.Horizontal);
        _explanation.MaximumSize = new Size(available, 0);
    }
}
