#Requires -Version 5.1
<#
.SYNOPSIS
  End-to-end test of watch_add / watch_list / watch_remove and the values that come with a stop, against a real Visual Studio.

.DESCRIPTION
  Starts Visual Studio (minimized, real profile) with this repository's solution, runs tests/e2e/watch.cs against it with the
  OrderDemo sample, and closes that Visual Studio again. It never answers a consent question and never clicks anything: Read and
  Control must be Allow (the defaults), which the driver checks first.
  It changes nothing in Visual Studio's settings. It refuses to run while any devenv.exe is running, and ends with a hygiene check
  (no Visual Studio, MCP server, temp folder or instance file left behind). The installed extension must be the build under test:
  run scripts/reinstall-vsix.ps1 -Build first. A run that cannot start leaves one screenshot, %TEMP%\debugviamcp-failure-watch.png.

.PARAMETER McpDll
  The DebugViaMcp.Mcp.dll the driver talks to (default: the Debug build in this repository).

.PARAMETER ReadyTimeoutSeconds
  How long to wait for the extension to report ready after Visual Studio started (default 120; a dialog in the way is detected after about 6 s).

.EXAMPLE
  powershell -File tests\e2e\watch.ps1
#>
param(
    [string]$McpDll,
    [int]$ReadyTimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $McpDll) { $McpDll = Join-Path $root 'src\DebugViaMcp.Mcp\bin\Debug\net10.0\DebugViaMcp.Mcp.dll' }

if (Get-Process devenv -ErrorAction SilentlyContinue) { throw 'Visual Studio is running. Close it first (this script never closes one it did not start).' }
if (-not (Test-Path $McpDll)) { throw "Not built: $McpDll (run scripts\build.ps1 first)." }

# Visual Studio keeps its settings and its instance files in the real profile; a shell with a redirected profile would look elsewhere.
$realHome = [Environment]::GetFolderPath('UserProfile')
$env:USERPROFILE = $realHome
$env:LOCALAPPDATA = Join-Path $realHome 'AppData\Local'
$env:APPDATA = Join-Path $realHome 'AppData\Roaming'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$install = & $vswhere -latest -prerelease -products * -property installationPath | Select-Object -First 1
$devenv = Join-Path $install 'Common7\IDE\devenv.exe'
$instances = Join-Path $env:LOCALAPPDATA 'DebugViaMcp\instances'
$solution = Join-Path $root 'DebugViaMcp.slnx'

Add-Type -Namespace E2e -Name Win -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowEnabled(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetWindow(System.IntPtr h, uint cmd);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int max);
'@
function Get-ModalDialog([System.Diagnostics.Process]$Process) {
    $Process.Refresh()
    $main = $Process.MainWindowHandle
    if ($main -eq [IntPtr]::Zero -or [E2e.Win]::IsWindowEnabled($main)) { return $null }
    $popup = [E2e.Win]::GetWindow($main, 6)   # GW_ENABLEDPOPUP
    if ($popup -eq [IntPtr]::Zero) { return '(untitled dialog)' }
    $title = New-Object System.Text.StringBuilder 256
    [void][E2e.Win]::GetWindowText($popup, $title, 256)
    if ($title.Length -gt 0) { $title.ToString() } else { '(untitled dialog)' }
}

function Save-FailureScreenshot {
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bitmap.Size)
        $file = Join-Path ([IO.Path]::GetTempPath()) 'debugviamcp-failure-watch.png'
        $bitmap.Save($file)
        $graphics.Dispose(); $bitmap.Dispose()
        Write-Host "screenshot of the screen at the failure: $file (delete it when you have looked)" -ForegroundColor Yellow
    }
    catch { Write-Host "(no screenshot: $($_.Exception.Message))" }
}

function Stop-OwnVs([int]$ProcessId) {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { return }
    [void]$p.CloseMainWindow()
    if (-not $p.WaitForExit(60000)) {
        Write-Warning "Visual Studio $ProcessId did not close in 60 s (a dialog may be open); stopping that process."
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
}

$exit = -1
$startedId = 0
try {
    $vs = Start-Process -FilePath $devenv -ArgumentList "`"$solution`"" -WindowStyle Minimized -PassThru
    $startedId = $vs.Id
    $ready = $null
    $dialogPolls = 0
    for ($i = 0; $i -lt ($ReadyTimeoutSeconds / 3) -and -not $ready; $i++) {
        Start-Sleep -Seconds 3
        if ($vs.HasExited) { throw "Visual Studio exited early (code $($vs.ExitCode))." }
        $dialog = Get-ModalDialog $vs
        if ($dialog) { $dialogPolls++ } else { $dialogPolls = 0 }
        if ($dialogPolls -ge 2) { throw "Visual Studio is waiting behind a dialog: '$dialog'. Nothing can load until someone answers it." }
        $file = Join-Path $instances "$($vs.Id).json"
        if (Test-Path $file) { $ready = Get-Content $file -Raw | ConvertFrom-Json }
    }
    if (-not $ready) { throw "The extension did not report ready within $ReadyTimeoutSeconds s (is the build under test installed? scripts\reinstall-vsix.ps1 -Build)." }
    if ($ready.protocolMinor -lt 9) { throw "The installed extension speaks protocol $($ready.protocolMajor).$($ready.protocolMinor); this test needs 2.9." }
    Write-Host "Visual Studio $($ready.vsVersion), extension $($ready.vsixVersion), protocol $($ready.protocolMajor).$($ready.protocolMinor), solution $($ready.solutionPath)"

    Push-Location $root
    try { & dotnet run tests/e2e/watch.cs -- $McpDll; $exit = $LASTEXITCODE }
    finally { Pop-Location }
}
catch {
    Write-Host "the run could not start: $($_.Exception.Message)" -ForegroundColor Red
    Save-FailureScreenshot
}
finally {
    if ($startedId -ne 0) { Stop-OwnVs $startedId }
}

# Hygiene: the run must leave the machine as it found it.
$leftovers = [System.Collections.Generic.List[string]]::new()
Get-Process devenv -ErrorAction SilentlyContinue | ForEach-Object { $leftovers.Add("devenv process $($_.Id) is still running") }
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -match 'DebugViaMcp\.Mcp\.dll' } |
    ForEach-Object { $leftovers.Add("MCP server process $($_.ProcessId) is still running") }
Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'debugviamcp-e2e-*' -ErrorAction SilentlyContinue |
    ForEach-Object { $leftovers.Add("temp folder $($_.FullName) was left behind") }
if ($startedId -ne 0) {
    Get-ChildItem $instances -File -Filter "$startedId.json" -ErrorAction SilentlyContinue | ForEach-Object { $leftovers.Add("instance file $($_.Name) of a Visual Studio that is gone") }
}
foreach ($l in $leftovers) { Write-Host "LEFT BEHIND: $l" -ForegroundColor Red }
if ($leftovers.Count -eq 0) { Write-Host 'Hygiene: no process, temp folder or instance file left behind.' -ForegroundColor Green }

Write-Host ''
Write-Host $(if ($exit -eq 0) { 'watch  PASSED' } else { "watch  FAILED (exit $exit)" }) -ForegroundColor $(if ($exit -eq 0) { 'Green' } else { 'Red' })
if ($exit -ne 0 -or $leftovers.Count -gt 0) { exit 1 }
