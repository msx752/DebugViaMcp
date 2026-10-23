#Requires -Version 5.1
<#
.SYNOPSIS
  End-to-end test of exception_list / exception_set / exception_reset against a real Visual Studio.

.DESCRIPTION
  For each phase it starts Visual Studio (minimized, real profile) with this repository's solution, runs tests/e2e/exception-settings.cs
  against it, and closes that Visual Studio again. It never answers a consent question and never clicks anything.

    allow  Control = Allow (the default). The whole feature, two agents, a break, vs_release.
    deny   Control = Deny.
    ask    Control = Ask with the shortest timeout: nobody answers, so the question times out and denies.
    perf   (only when asked for) latency of every call and Visual Studio's memory over hundreds of calls and many connections.

  What it changes and puts back, whatever happens (try/finally):
    - the permission values in Visual Studio's settings.json (backed up byte for byte and restored),
    - samples/FaultDemo/Properties/launchSettings.json ("crash 15"), removed again (an existing one is restored),
    - the Visual Studio it started and a FaultDemo it may have left running (only processes it started itself).
  It refuses to run while any devenv.exe is running: close your Visual Studio first, as scripts/reinstall-vsix.ps1 does.
  The installed extension must be the build under test: run scripts/reinstall-vsix.ps1 -Build first.

.PARAMETER Phases
  Which phases to run (default: allow, deny, ask).

.PARAMETER McpDll
  The DebugViaMcp.Mcp.dll the driver talks to (default: the Debug build in this repository).

.PARAMETER ReadyTimeoutSeconds
  How long to wait for the extension to report ready after Visual Studio started (default 120; a dialog in the way is detected after about 6 s).

.EXAMPLE
  powershell -File tests\e2e\exception-settings.ps1 -Phases allow
#>
param(
    [ValidateSet('allow', 'deny', 'ask', 'perf')][string[]]$Phases = @('allow', 'deny', 'ask'),
    [string]$McpDll,
    [int]$ReadyTimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $McpDll) { $McpDll = Join-Path $root 'src\DebugViaMcp.Mcp\bin\Debug\net10.0\DebugViaMcp.Mcp.dll' }

if (Get-Process devenv -ErrorAction SilentlyContinue) { throw 'Visual Studio is running. Close it first (this script never closes one it did not start).' }
if (-not (Test-Path $McpDll)) { throw "Not built: $McpDll (run scripts\build.ps1 first)." }
if (-not (Test-Path (Join-Path $root 'samples\FaultDemo\bin\Debug\net10.0\FaultDemo.exe'))) { throw 'samples\FaultDemo is not built (dotnet build samples\FaultDemo).' }

# Visual Studio keeps its settings and its instance files in the real profile; a shell with a redirected profile would look elsewhere.
$realHome = [Environment]::GetFolderPath('UserProfile')
$env:USERPROFILE = $realHome
$env:LOCALAPPDATA = Join-Path $realHome 'AppData\Local'
$env:APPDATA = Join-Path $realHome 'AppData\Roaming'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$install = & $vswhere -latest -prerelease -products * -property installationPath | Select-Object -First 1
$devenv = Join-Path $install 'Common7\IDE\devenv.exe'
$settingsFile = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio') -Directory -Filter '18.0_*' |
    Where-Object { $_.Name -notlike '*Exp' } | ForEach-Object { Join-Path $_.FullName 'settings.json' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $settingsFile) { throw 'Visual Studio 18 settings.json not found (start Visual Studio once).' }
$instances = Join-Path $env:LOCALAPPDATA 'DebugViaMcp\instances'
$solution = Join-Path $root 'DebugViaMcp.slnx'
$launchDir = Join-Path $root 'samples\FaultDemo\Properties'
$launchFile = Join-Path $launchDir 'launchSettings.json'

$settingsBackup = [IO.File]::ReadAllBytes($settingsFile)
$launchBackup = if (Test-Path $launchFile) { [IO.File]::ReadAllBytes($launchFile) } else { $null }
$launchDirExisted = Test-Path $launchDir
$started = [System.Collections.Generic.List[int]]::new()
$results = [ordered]@{}

function Write-PermissionSettings([string]$Control, [int]$ConsentSeconds) {
    # Starts from the user's own file every time. Allow is the default, so it needs no entry.
    [IO.File]::WriteAllBytes($settingsFile, $settingsBackup)
    if (-not $Control) { return }
    $lines = [IO.File]::ReadAllLines($settingsFile)
    $open = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l.Trim() -eq '{' })
    if ($open -lt 0) { throw "Unexpected settings.json: no opening brace line." }
    $new = @($lines[0..$open]) + "  `"debugging.mcp.control`": `"$Control`"," + "  `"debugging.mcp.consentTimeoutSeconds`": $ConsentSeconds," + @($lines[($open + 1)..($lines.Length - 1)])
    [IO.File]::WriteAllLines($settingsFile, $new)
}

# A modal dialog (a project upgrade prompt, a license box) disables Visual Studio's main window and is its enabled popup: the same test the
# extension's own dialog check uses. Without it a run would wait out the whole timeout for an extension that cannot load behind a dialog.
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

function Stop-OwnVs([int]$ProcessId) {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { return }
    [void]$p.CloseMainWindow()
    if (-not $p.WaitForExit(60000)) {
        Write-Warning "Visual Studio $ProcessId did not close in 60 s (a dialog may be open); stopping that process."
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
}

try {
    New-Item -ItemType Directory -Force $launchDir | Out-Null
    Set-Content -Path $launchFile -Encoding utf8 -Value '{"profiles":{"FaultDemo":{"commandName":"Project","commandLineArgs":"crash 15"}}}'

    foreach ($phase in $Phases) {
        Write-Host "=== phase: $phase ===" -ForegroundColor Cyan
        switch ($phase) {
            'allow' { Write-PermissionSettings $null 0 }
            'perf' { Write-PermissionSettings $null 0 }
            'deny' { Write-PermissionSettings 'Deny' 15 }
            'ask' { Write-PermissionSettings 'Ask' 15 }
        }

        $vs = Start-Process -FilePath $devenv -ArgumentList "`"$solution`"" -WindowStyle Minimized -PassThru
        $started.Add($vs.Id)
        try {
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
            if ($ready.protocolMinor -lt 8) { throw "The installed extension speaks protocol $($ready.protocolMajor).$($ready.protocolMinor); this test needs 2.8." }
            Write-Host "Visual Studio $($ready.vsVersion), extension $($ready.vsixVersion), protocol $($ready.protocolMajor).$($ready.protocolMinor), solution $($ready.solutionPath)"

            Push-Location $root
            try { & dotnet run tests/e2e/exception-settings.cs -- $McpDll $phase; $results[$phase] = $LASTEXITCODE }
            finally { Pop-Location }
        }
        catch {
            Write-Host "phase $phase could not run: $($_.Exception.Message)" -ForegroundColor Red
            $results[$phase] = -1
        }
        finally {
            Stop-OwnVs $vs.Id
            # A program the debugger started is stopped with the session; one left behind is ours only if it started after we did.
            Get-Process FaultDemo -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $vs.StartTime } | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}
finally {
    [IO.File]::WriteAllBytes($settingsFile, $settingsBackup)
    if ($null -ne $launchBackup) { [IO.File]::WriteAllBytes($launchFile, $launchBackup) }
    elseif (Test-Path $launchFile) { Remove-Item $launchFile -Force }
    if (-not $launchDirExisted -and (Test-Path $launchDir) -and -not (Get-ChildItem $launchDir -Force)) { Remove-Item $launchDir -Force }
    foreach ($id in $started) { Stop-OwnVs $id }
}

# Hygiene: the run must leave the machine as it found it.
$leftovers = [System.Collections.Generic.List[string]]::new()
foreach ($name in 'devenv', 'FaultDemo') {
    Get-Process $name -ErrorAction SilentlyContinue | ForEach-Object { $leftovers.Add("$name process $($_.Id) is still running") }
}
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -match 'DebugViaMcp\.Mcp\.dll' } |
    ForEach-Object { $leftovers.Add("MCP server process $($_.ProcessId) is still running") }
Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'debugviamcp-e2e-*' -ErrorAction SilentlyContinue |
    ForEach-Object { $leftovers.Add("temp folder $($_.FullName) was left behind") }
Get-ChildItem $instances -File -Filter '*.json' -ErrorAction SilentlyContinue | Where-Object { $started -contains [int]$_.BaseName } |
    ForEach-Object { $leftovers.Add("instance file $($_.Name) of a Visual Studio that is gone") }
if (-not [System.Linq.Enumerable]::SequenceEqual([byte[]][IO.File]::ReadAllBytes($settingsFile), [byte[]]$settingsBackup)) { $leftovers.Add('settings.json is not as it was') }
if ((Test-Path $launchFile) -and $null -eq $launchBackup) { $leftovers.Add('launchSettings.json was left behind') }
foreach ($l in $leftovers) { Write-Host "LEFT BEHIND: $l" -ForegroundColor Red }
if ($leftovers.Count -eq 0) { Write-Host 'Hygiene: no process, temp folder, instance file or setting left behind.' -ForegroundColor Green }

Write-Host ''
$failed = $leftovers.Count -gt 0
foreach ($phase in $results.Keys) {
    $ok = $results[$phase] -eq 0
    if (-not $ok) { $failed = $true }
    Write-Host ("{0,-6} {1}" -f $phase, $(if ($ok) { 'PASSED' } else { "FAILED (exit $($results[$phase]))" })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
if ($failed) { exit 1 }
