using System;
using System.Threading;
using System.Threading.Tasks;

namespace DebugViaMcp.Vsix.Core;

/// <summary>Marshals work to the VS UI thread. Implementations must fail with BridgeErrorCode.Busy after 10 s.</summary>
public interface IUiThread
{
    Task<T> RunAsync<T>(Func<T> work, CancellationToken ct);
}
