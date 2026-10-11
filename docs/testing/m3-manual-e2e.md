# M3 end-to-end test

Same setup as [m2-manual-e2e.md](m2-manual-e2e.md) (build, install the 0.3.0 VSIX with the real profile
environment, open `samples/OrderDemo/OrderDemo.csproj`, OrderDemo = startup project). The Output pane shows
`VsMcpDebugger 0.3.0 ready (…, protocol 2.1)`.

## Automated driver

    dotnet run scripts/e2e/m3-e2e.cs -- <repo>\src\VsMcpDebugger.Mcp\bin\Debug\net10.0\VsMcpDebugger.Mcp.dll

It builds and starts `samples/FaultDemo` for the attach and exception steps and answers info bars with
`scripts/e2e/click-infobar.ps1`.

## Manual checks

| # | Action | Expected |
|---|---|---|
| 1 | `debug_attach` to FaultDemo | info bar: "… attach the debugger to FaultDemo (pid N) — Visual Studio will be able to read and change that process's memory" |
| 2 | `bp_enable` true on a conditional breakpoint | info bar: "… enable the breakpoint at … when … — this runs in your program on every hit" |
| 3 | `output_read` pane "VsMcpDebugger Activity" | `denied` / `policy` |
| 4 | Tools → Import and Export Settings: import a file with Mutate = Deny | the next `set_value` is denied by settings without restarting; Activity shows "Imported settings: Mutate: Ask → Deny" |
| 5 | A `set_value` whose expression is 500 characters long | the info bar wraps or truncates visibly; Allow/Deny buttons stay reachable |
| 6 | Click another frame in the Call Stack window while a `set_value` question is open, then Allow | `wrong_mode` "program state changed", nothing assigned |

## Run record

(filled in by Task 16)
