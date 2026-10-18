using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix.Ui;

/// <summary>
/// "Debugging via MCP: claude-code connected" / "Debugging via MCP: AI disconnected" in the status bar; click opens the
/// Activity pane. Visual Studio has no public API for clickable status-bar items, so the text is added to the
/// status bar's WPF panel, found once at startup. If that panel cannot be found the indicator stays off (logged
/// once); the Extensions menu and the Activity pane keep working. Updates only on access changes — no polling.
/// </summary>
internal sealed class StatusBarIndicator
{
    private readonly AiAccessSwitch _access;
    private readonly InstanceLease _lease;
    private readonly Func<bool> _enabled;
    private readonly TextBlock _text;
    private bool _textOnly;
    private bool _textSet;

    private StatusBarIndicator(AiAccessSwitch access, InstanceLease lease, JoinableTaskFactory jtf, Func<bool> enabled, ActivityPane activity)
    {
        _access = access;
        _lease = lease;
        _enabled = enabled;
        _text = new TextBlock { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };
        _text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.StatusBarDefaultTextBrushKey);
        _text.MouseLeftButtonUp += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            activity.Show();
        };
        EventHandler refreshOnMainThread = (_, _) => jtf.RunAsync(async () =>
        {
            await jtf.SwitchToMainThreadAsync();
            Refresh();
        }).FileAndForget("DebugViaMcp/StatusBarIndicator/Refresh");
        _access.Changed += refreshOnMainThread;
        _lease.Changed += refreshOnMainThread;
    }

    public static StatusBarIndicator? TryCreate(AiAccessSwitch access, InstanceLease lease, JoinableTaskFactory jtf, Func<bool> enabled, ActivityPane activity, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var panel = FindStatusBarPanel(Application.Current?.MainWindow);
        var indicator = new StatusBarIndicator(access, lease, jtf, enabled, activity);
        if (panel is null)
        {
            // Newer Visual Studio builds lay the status bar out differently. Fall back to the standard status bar text:
            // it is shown when something changes (a client connects or disconnects, the lease moves) but is not clickable.
            log.Warn("Status bar panel not found (Visual Studio changed its layout); showing the status as plain status bar text instead.");
            indicator._textOnly = true;
            indicator.Refresh();
            return indicator;
        }

        DockPanel.SetDock(indicator._text, Dock.Right);
        panel.Children.Insert(0, indicator._text);
        indicator.Refresh();
        return indicator;
    }

    public void Refresh()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var clients = _access.Clients;
        string? text = !_enabled() ? null
            : !_access.Enabled ? "Debugging via MCP: AI disconnected"
            : clients.Count > 0 ? $"Debugging via MCP: {string.Join(", ", clients.Distinct())} connected" + (_lease.Holder is { } holder ? $" — in use by {holder.Client}" : "")
            : null;
        if (_textOnly)
        {
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar bar && (text is not null || _textSet))
                bar.SetText(text ?? "");
            _textSet = text is not null;
            return;
        }

        _text.Text = text ?? "";
        _text.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static DockPanel? FindStatusBarPanel(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is DockPanel { Name: "StatusBarPanel" } panel) return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindStatusBarPanel(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }

        return null;
    }
}
