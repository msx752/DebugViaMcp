using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using VsMcpDebugger.Vsix.Core;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;

namespace VsMcpDebugger.Vsix.Ui;

/// <summary>
/// "&lt;project&gt; is out of date. Rebuild?" as an info bar on the Visual Studio main window, one per project, with a
/// Rebuild button and the usual close ✕. It is an offer only: nothing happens unless the owner presses Rebuild.
/// </summary>
internal sealed class InfoBarRebuildPrompt(IAsyncServiceProvider services, JoinableTaskFactory jtf) : IRebuildPrompt
{
    private readonly Dictionary<string, Bar> _bars = [];
    private readonly Dictionary<string, int> _generation = [];   // bumped by every Hide, so a Show that was overtaken by it gives up

    /// <summary>Called with the project when the owner closes its bar with ✕.</summary>
    public Action<string>? OnDismissed { get; set; }

    public void Show(string project, string text, Action rebuild)
    {
        int generation;
        lock (_generation) generation = _generation.TryGetValue(project, out var g) ? g : 0;
        jtf.RunAsync(async () =>
        {
            await jtf.SwitchToMainThreadAsync();
            if (_bars.ContainsKey(project)) return;

            var shell = (IVsShell)(await services.GetServiceAsync(typeof(SVsShell)))!;
            var factory = (IVsInfoBarUIFactory)(await services.GetServiceAsync(typeof(SVsInfoBarUIFactory)))!;
            ErrorHandler.ThrowOnFailure(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject));
            var host = (IVsInfoBarHost)hostObject;
            lock (_generation)
            {
                if ((_generation.TryGetValue(project, out var now) ? now : 0) != generation) return;   // hidden while we were waiting
            }

            var model = new InfoBarModel([new InfoBarTextSpan(text)], [new InfoBarButton("Rebuild")], KnownMonikers.StatusWarning, isCloseButtonVisible: true);
            var element = factory.CreateInfoBar(model);
            var bar = new Bar(element, project, rebuild, this);
            element.Advise(bar, out bar.Cookie);
            host.AddInfoBar(element);
            _bars[project] = bar;
        }).FileAndForget("VsMcpDebugger/RebuildPrompt/Show");
    }

    public void Hide(string project)
    {
        lock (_generation) _generation[project] = (_generation.TryGetValue(project, out var g) ? g : 0) + 1;
        jtf.RunAsync(async () =>
        {
            await jtf.SwitchToMainThreadAsync();
            if (!_bars.TryGetValue(project, out var bar)) return;
            _bars.Remove(project);
            bar.Close();
        }).FileAndForget("VsMcpDebugger/RebuildPrompt/Hide");
    }

    private sealed class Bar(IVsInfoBarUIElement element, string project, Action rebuild, InfoBarRebuildPrompt owner) : IVsInfoBarUIEvents
    {
        public uint Cookie;
        private bool _closedByUs;

        public void Close()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _closedByUs = true;
            element.Unadvise(Cookie);
            element.Close();
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_closedByUs) return;
            owner._bars.Remove(project);
            owner.OnDismissed?.Invoke(project);
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            rebuild();
        }
    }
}
