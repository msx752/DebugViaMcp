# THROWAWAY SPIKE — evidence for docs/spikes/2026-10-11-nse.md
param([string]$ProgId = 'VisualStudio.DTE.18.0', $Dte = $null)
$dte = if ($Dte) { $Dte } else { [System.Runtime.InteropServices.Marshal]::GetActiveObject($ProgId) }
if ($dte.Debugger.CurrentMode -ne 2) { throw 'VS must be in Break mode inside OrderService.Submit.' }  # 2 = dbgBreakMode
function Eval([string]$e) { $x = $dte.Debugger.GetExpression($e, $true, 5000); "{0,-28} valid={1,-5} value={2}" -f $e, $x.IsValidValue, $x.Value }
Eval 'order.TouchCount'
Eval 'order.Touch(), nse'
Eval 'order.TouchCount'
Eval 'order.Touch()'
Eval 'order.TouchCount'
Eval 'order.Note.Length, nse'
