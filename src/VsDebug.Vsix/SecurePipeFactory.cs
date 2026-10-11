using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using VsDebug.Vsix.Core;

namespace VsDebug.Vsix;

/// <summary>Pipe accessible only by the current user; network access explicitly denied.</summary>
internal sealed class SecurePipeFactory : IPipeStreamFactory
{
    public Stream Create(string pipeName)
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    public Task WaitForConnectionAsync(Stream stream, CancellationToken ct) =>
        ((NamedPipeServerStream)stream).WaitForConnectionAsync(ct);
}
