using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using DebugViaMcp.Vsix.Core.Setup;

namespace DebugViaMcp.Vsix.Ui;

/// <summary>
/// Extensions → Debugging via MCP → Set Up AI Agent: what the MCP server needs on this machine, and, for the AI client
/// the user picks, the command or configuration to copy. It runs nothing itself.
/// </summary>
internal sealed class McpSetupDialog : DialogWindow
{
    private readonly IReadOnlyList<AgentClient> _clients;
    private readonly TextBlock _where = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBox _text = new()
    {
        IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), MinHeight = 120,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private readonly Button _copy = new() { Content = "Copy", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private readonly TextBlock _copied = new() { VerticalAlignment = VerticalAlignment.Center };

    public McpSetupDialog(string status, IReadOnlyList<AgentClient> clients)
    {
        _clients = clients;
        Title = "Debugging via MCP - Set up AI agent";
        Width = 760;
        Height = 560;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        HasMaximizeButton = false;
        HasMinimizeButton = false;
        SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
        SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

        var list = new ListBox { MinHeight = 90, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var client in clients) list.Items.Add(client.Name);
        list.SelectionChanged += (_, _) => ShowClient(list.SelectedIndex);

        _copy.Click += (_, _) => Copy();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "Close", MinWidth = 90, IsCancel = true, IsDefault = true };
        buttons.Children.Add(_copied);
        buttons.Children.Add(new Border { Width = 12 });
        buttons.Children.Add(_copy);
        buttons.Children.Add(close);

        var panel = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = status, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock
        {
            Text = "Which AI client do you want to set up? Debugging via MCP works with Visual Studio only; the client itself can run anywhere.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
        });
        top.Children.Add(list);
        top.Children.Add(_where);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(_text);
        Content = panel;
        list.SelectedIndex = 0;
    }

    private void ShowClient(int index)
    {
        _copied.Text = "";
        if (index < 0 || index >= _clients.Count) return;
        _where.Text = _clients[index].Where;
        _text.Text = _clients[index].Text;
        _copy.IsEnabled = true;
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetText(_text.Text);
            _copied.Text = "Copied to the clipboard.";
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard: the text stays selectable in the box.
            _copied.Text = "The clipboard is busy; select the text and copy it yourself.";
            _text.Focus();
            _text.SelectAll();
        }
    }
}
