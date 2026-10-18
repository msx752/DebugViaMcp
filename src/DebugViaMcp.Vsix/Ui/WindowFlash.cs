using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DebugViaMcp.Vsix.Ui;

/// <summary>
/// Makes Visual Studio's taskbar button flash until the window is activated. A consent question that appears while
/// Visual Studio is in the background (another window or monitor) is otherwise easy to miss, and no answer means deny.
/// </summary>
internal static class WindowFlash
{
    private const uint FlashTray = 0x2;
    private const uint FlashUntilForeground = 0xC;   // FLASHW_TIMERNOFG

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>Flashes the main window of this Visual Studio unless it is already in front. Never throws.</summary>
    public static void FlashVisualStudio()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero || GetForegroundWindow() == window) return;
            var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = FlashTray | FlashUntilForeground, Count = uint.MaxValue, Timeout = 0 };
            FlashWindowEx(ref info);
        }
        catch (InvalidOperationException)
        {
            // The window flash is a courtesy; the question itself is unaffected.
        }
    }
}
