using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DebugViaMcp.Vsix.Infrastructure;

/// <summary>Whether a process runs as the same user as Visual Studio, by token SID (spec §3.4).</summary>
internal static class ProcessOwner
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;

    public static bool IsCurrentUser(int pid)
    {
        var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!NativeMethods.OpenProcessToken(process, TokenQuery, out var token)) return false;
            try
            {
                using var owner = new WindowsIdentity(token);
                using var current = WindowsIdentity.GetCurrent();
                return owner.User is not null && owner.User == current.User;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("advapi32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("kernel32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
