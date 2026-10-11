# VsMcpDebugger

MCP server that lets AI coding agents such as Claude Code drive the Visual Studio debugger: breakpoints, stepping,
call stacks, variables and evaluation. Anything that would change your program asks for your approval in Visual
Studio first.

## Install

1. Install the **VsMcpDebugger** extension in Visual Studio 2022 (17.14+) or Visual Studio 2026.
2. Install this tool and register it with Claude Code:

       dotnet tool install -g VsMcpDebugger
       claude mcp add vsmcpdebugger -- vsmcpdebugger serve

3. Open your solution in Visual Studio and ask Claude, for example: *"Break in OrderService.Submit when total > 1000
   and show me the order."*

Requires the .NET 10 runtime. The tool talks to Visual Studio only through a local, per-user named pipe — no network.

## More

Documentation, security model and source: https://github.com/msx752/VsMcpDebugger
