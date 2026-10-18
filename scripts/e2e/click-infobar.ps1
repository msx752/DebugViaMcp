# Clicks a Debugging via MCP consent info-bar button ("Allow once", "Allow for session", "Deny") in the running
# Visual Studio via UI Automation. Test helper for scripts/e2e/m2-e2e.cs; never used by the product.
# Waits 1.5 s after the button appears: the extension ignores Allow clicks during the first second.
param([Parameter(Mandatory)][string]$Button, [int]$TimeoutSec = 20)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$byName = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Button)
while ((Get-Date) -lt $deadline) {
    foreach ($vs in Get-Process devenv -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0) {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($vs.MainWindowHandle)
        $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName)
        if ($element) {
            Start-Sleep -Milliseconds 1500
            $pattern = $null
            if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
                $pattern.Invoke()
                exit 0
            }
        }
    }
    Start-Sleep -Milliseconds 300
}
Write-Error "No '$Button' button found within $TimeoutSec s."
exit 1
