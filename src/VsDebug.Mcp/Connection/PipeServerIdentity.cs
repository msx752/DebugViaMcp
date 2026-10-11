using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VsDebug.Mcp.Connection;

/// <summary>
/// Pipe-squatting guard: confirms that the process serving a connected pipe is the one recorded in
/// the instance file, before any secret is sent. Windows only; on Unix the socket lives in a 0700
/// directory owned by the user (spec §2.4).
/// </summary>
internal static partial class PipeServerIdentity
{
    public static bool IsServedBy(NamedPipeClientStream stream, int expectedPid)
    {
        if (!OperatingSystem.IsWindows()) return true;
        return GetNamedPipeServerProcessId(stream.SafePipeHandle, out var actual) && actual == (uint)expectedPid;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
