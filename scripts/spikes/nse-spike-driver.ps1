# THROWAWAY SPIKE DRIVER — launches VS 2026 on OrderDemo, breaks in OrderService.Submit, runs nse-spike.ps1.
# Windows PowerShell 5.1 only (Marshal.GetActiveObject).
param([string]$DevEnv = 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe',
      [string]$ProgId = 'VisualStudio.DTE.18.0')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$proj = Join-Path $root 'samples\OrderDemo\OrderDemo.csproj'
$file = Join-Path $root 'samples\OrderDemo\OrderService.cs'

# COM calls into a busy VS can be rejected (RPC_E_CALL_REJECTED); retry them.
function Retry([scriptblock]$Block, [int]$Seconds = 240) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ($true) {
        try { $r = & $Block; if ($null -ne $r -and $r -ne $false) { return $r } } catch { }
        if ((Get-Date) -gt $deadline) { throw "Timed out: $Block" }
        Start-Sleep -Milliseconds 1000
    }
}

$vs = Start-Process $DevEnv -ArgumentList "`"$proj`"" -PassThru
try {
    $dte = Retry { [System.Runtime.InteropServices.Marshal]::GetActiveObject($ProgId) }
    Retry { $dte.Solution.IsOpen -and $dte.Solution.Projects.Count -gt 0 } | Out-Null
    Start-Sleep 5
    Retry { $dte.Debugger.Breakpoints.Add('', $file, 7) } | Out-Null
    Retry { $dte.Debugger.Go($false); $true } | Out-Null
    Retry { $dte.Debugger.CurrentMode -eq 2 } | Out-Null
    "VS version: " + $dte.Version
    "Stopped at: " + $dte.Debugger.CurrentStackFrame.FunctionName
    & (Join-Path $PSScriptRoot 'nse-spike.ps1') -Dte $dte
}
finally {
    try { $dte.Debugger.Stop($true) } catch { }
    try { foreach ($b in @($dte.Debugger.Breakpoints)) { $b.Delete() } } catch { }
    try { $dte.Quit() } catch { }
    Start-Sleep 3
    if (-not $vs.HasExited) { Stop-Process -Id $vs.Id -Force }
}
