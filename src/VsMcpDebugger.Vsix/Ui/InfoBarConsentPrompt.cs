using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using VsMcpDebugger.Vsix.Core;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;

namespace VsMcpDebugger.Vsix.Ui;

/// <summary>Consent question as an info bar on the Visual Studio main window. Closing it with ✕ counts as Deny.</summary>
internal sealed class InfoBarConsentPrompt(IAsyncServiceProvider services, JoinableTaskFactory jtf) : IConsentPrompt
{
    public async Task<ConsentChoice> AskAsync(string text, TimeSpan timeout, CancellationToken ct)
    {
        await jtf.SwitchToMainThreadAsync(ct);
        var shell = (IVsShell)(await services.GetServiceAsync(typeof(SVsShell)))!;
        var factory = (IVsInfoBarUIFactory)(await services.GetServiceAsync(typeof(SVsInfoBarUIFactory)))!;
        ErrorHandler.ThrowOnFailure(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject));
        var host = (IVsInfoBarHost)hostObject;

        var seconds = ((int)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        var model = new InfoBarModel(
            [new InfoBarTextSpan(text + $"   (no answer in {seconds} s = deny)")],
            [
                new InfoBarButton("Allow once", ConsentChoice.AllowOnce),
                new InfoBarButton("Allow for session", ConsentChoice.AllowSession),
                new InfoBarButton("Deny", ConsentChoice.Deny),
            ],
            KnownMonikers.StatusSecurityWarning,
            isCloseButtonVisible: true);

        var element = factory.CreateInfoBar(model);
        var events = new Events();
        element.Advise(events, out var cookie);
        host.AddInfoBar(element);
        try
        {
            using (ct.Register(() => events.Cancel(ct)))
            {
#pragma warning disable VSTHRD003 // Completed by the info bar's own UI events or by cancellation; nothing here blocks the UI thread.
                return await events.Result.Task;
#pragma warning restore VSTHRD003
            }
        }
        finally
        {
            await jtf.SwitchToMainThreadAsync(CancellationToken.None);
            element.Unadvise(cookie);
            element.Close();
        }
    }

    private sealed class Events : IVsInfoBarUIEvents
    {
        public TaskCompletionSource<ConsentChoice> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Cancel(CancellationToken ct) => Result.TrySetCanceled(ct);

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement) => Result.TrySetResult(ConsentChoice.Deny);

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem) =>
            Result.TrySetResult(actionItem is { ActionContext: ConsentChoice choice } ? choice : ConsentChoice.Deny);
    }
}
