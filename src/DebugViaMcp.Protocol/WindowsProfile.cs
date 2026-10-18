using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DebugViaMcp.Protocol;

/// <summary>
/// Resolves the current user's real local application data directory on Windows from the user's profile
/// (GetUserProfileDirectory on the process token), ignoring USERPROFILE/LOCALAPPDATA overrides. This keeps a
/// normally started Visual Studio and an MCP host launched from a customized shell pointing at the same home.
/// </summary>
public static class WindowsProfile
{
    /// <returns>The path, or null when not on Windows or the profile cannot be read.</returns>
    public static string? LocalAppData()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeMethods.TokenQuery, out var token)) return null;
        try
        {
            uint size = 0;
            NativeMethods.GetUserProfileDirectoryW(token, null, ref size);
            if (size == 0) return null;
            var buffer = new char[size];
            if (!NativeMethods.GetUserProfileDirectoryW(token, buffer, ref size)) return null;
            var profile = new string(buffer, 0, (int)size - 1);
            return Path.Combine(profile, "AppData", "Local");
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static class NativeMethods
    {
        public const uint TokenQuery = 0x0008;

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetUserProfileDirectoryW(IntPtr token, [Out] char[]? profileDir, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
