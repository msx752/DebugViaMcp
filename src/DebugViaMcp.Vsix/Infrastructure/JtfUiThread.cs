using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;

namespace DebugViaMcp.Vsix.Infrastructure;

internal sealed class JtfUiThread(JoinableTaskFactory jtf) : IUiThread
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            await jtf.SwitchToMainThreadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BridgeException(BridgeErrorCode.Busy, "Visual Studio's UI thread is busy (build or modal dialog?).", "Close any open dialog in Visual Studio and retry.");
        }

        return work();
    }
}
