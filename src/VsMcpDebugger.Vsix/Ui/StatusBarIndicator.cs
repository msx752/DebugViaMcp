using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Ui;

/// <summary>
/// "VsMcpDebugger: claude-code connected" / "VsMcpDebugger: AI disconnected" in the status bar; click opens the
/// Activity pane. Visual Studio has no public API for clickable status-bar items, so the text is added to the
/// status bar's WPF panel, found once at startup. If that panel cannot be found the indicator stays off (logged
/// once); the Extensions menu and the Activity pane keep working. Updates only on access changes — no polling.
/// </summary>
internal sealed class StatusBarIndicator
{
    private readonly AiAccessSwitch _access;
    private readonly Func<bool> _enabled;
    private readonly TextBlock _text;

    private StatusBarIndicator(AiAccessSwitch access, JoinableTaskFactory jtf, Func<bool> enabled, ActivityPane activity)
    {
        _access = access;
        _enabled = enabled;
        _text = new TextBlock { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };
        _text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.StatusBarDefaultTextBrushKey);
        _text.MouseLeftButtonUp += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            activity.Show();
        };
        _access.Changed += (_, _) => jtf.RunAsync(async () =>
        {
            await jtf.SwitchToMainThreadAsync();
            Refresh();
        }).FileAndForget("VsMcpDebugger/StatusBarIndicator/Refresh");
    }

    public static StatusBarIndicator? TryCreate(AiAccessSwitch access, JoinableTaskFactory jtf, Func<bool> enabled, ActivityPane activity, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var panel = FindStatusBarPanel(Application.Current?.MainWindow);
        if (panel is null)
        {
            log.Warn("Status bar indicator unavailable: the status bar panel was not found.");
            return null;
        }

        var indicator = new StatusBarIndicator(access, jtf, enabled, activity);
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
            : !_access.Enabled ? "VsMcpDebugger: AI disconnected"
            : clients.Count > 0 ? $"VsMcpDebugger: {string.Join(", ", clients.Distinct())} connected"
            : null;
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
