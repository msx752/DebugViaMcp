# Automated version of the manual consent checks (docs/testing/m3-manual-e2e.md row 6 and the instruction-pointer check).
# Opens one set_value question with scripts/e2e/m3-consent-check.cs, then changes the debugger state through Visual
# Studio's own automation interface (DTE), exactly what the user would do with the mouse, and answers "Allow once".
# Test helper only; never used by the product.
# Usage: powershell -File scripts/e2e/m3-consent-check.ps1 -Scenario frame|ip -Mcp <path-to-VsMcpDebugger.Mcp.dll>
param([Parameter(Mandatory)][ValidateSet('frame', 'ip')][string]$Scenario, [Parameter(Mandatory)][string]$Mcp)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$log = Join-Path ([IO.Path]::GetTempPath()) "m3-consent-check-$Scenario.log"
Remove-Item $log -ErrorAction SilentlyContinue

$dte = [Runtime.InteropServices.Marshal]::GetActiveObject('VisualStudio.DTE.18.0')
function Where-Am-I {
    $f = $dte.Debugger.CurrentStackFrame
    $caret = if ($dte.ActiveDocument) { $dte.ActiveDocument.Selection.CurrentLine } else { '?' }
    "{0}  (editor caret line {1})" -f $f.FunctionName, $caret
}

$driver = Start-Process dotnet -ArgumentList 'run', (Join-Path $repo 'scripts/e2e/m3-consent-check.cs'), '--', $Mcp, $Scenario `
    -RedirectStandardOutput $log -NoNewWindow -PassThru -WorkingDirectory $repo
for ($i = 0; $i -lt 90 -and -not (Select-String -Path $log -Pattern 'QUESTION OPEN' -Quiet -ErrorAction SilentlyContinue); $i++) { Start-Sleep 1 }
Start-Sleep 2   # let the info bar appear

"before : $(Where-Am-I)"
if ($Scenario -eq 'frame') {
    # Same as double-clicking the caller in the Call Stack window.
    $dte.Debugger.CurrentStackFrame = $dte.Debugger.CurrentThread.StackFrames.Item(2)
}
else {
    # Same as dragging the yellow arrow: move the caret, then Debug > Set Next Statement.
    $dte.ItemOperations.OpenFile((Join-Path $repo 'samples\OrderDemo\OrderService.cs')) | Out-Null
    $dte.ActiveDocument.Selection.GotoLine(21)
    $dte.ExecuteCommand('Debug.SetNextStatement')
}
Start-Sleep 1
"after  : $(Where-Am-I)"

& (Join-Path $PSScriptRoot 'click-infobar.ps1') -Button 'Allow once'
"clicked : Allow once"
$driver.WaitForExit(60000) | Out-Null
Get-Content $log | Select-String 'RESULT' | ForEach-Object { $_.Line }
Get-Content (Get-ChildItem "$env:LOCALAPPDATA\VsMcpDebugger\logs\audit-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName -Tail 6 |
    Where-Object { $_ -match '"operation":"set_value"' } | Select-Object -Last 1 | ForEach-Object { $_ -replace '.*("decision":"[^"]*","outcome":"[^"]*").*', 'audit  : $1' }
