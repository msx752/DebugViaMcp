using System.Diagnostics;
using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Protocol.Tests;

/// <summary>
/// The IDE and the MCP host must agree on the DebugViaMcp home even when one of them was started with a
/// redirected USERPROFILE/LOCALAPPDATA (custom launchers, sandboxes). On Windows the home therefore comes
/// from the user's real profile directory, not from environment variables. Windows resolves known folders
/// once per process, so the override is applied to a child process.
/// </summary>
public class WindowsProfileTests
{
    [Fact]
    public async Task Local_app_data_ignores_profile_environment_overrides()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only: Unix homes come from HOME/XDG by design.");
        var fake = Path.Combine(Path.GetTempPath(), "debugviamcp-fake-profile");
        var dll = typeof(WindowsProfile).Assembly.Location;

        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", $"Add-Type -Path '{dll}'; [DebugViaMcp.Protocol.WindowsProfile]::LocalAppData()" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["USERPROFILE"] = fake;
        psi.Environment["LOCALAPPDATA"] = Path.Combine(fake, "AppData", "Local");

        try
        {
            using var child = Process.Start(psi)!;
            var output = (await child.StandardOutput.ReadToEndAsync()).Trim();
            var error = await child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();

            Assert.True(child.ExitCode == 0, error);
            Assert.DoesNotContain("debugviamcp-fake-profile", output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(WindowsProfile.LocalAppData(), output, ignoreCase: true);
        }
        finally
        {
            // PowerShell creates its per-user folders under the fake profile it was given.
            if (Directory.Exists(fake)) Directory.Delete(fake, recursive: true);
        }
    }
}
