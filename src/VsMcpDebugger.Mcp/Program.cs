using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VsMcpDebugger.Mcp.Connection;
using VsMcpDebugger.Mcp.Discovery;
using VsMcpDebugger.Mcp.Logging;
using VsMcpDebugger.Mcp.Session;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;

var command = args.FirstOrDefault() ?? "serve";
if (command is not "serve")
{
    await Console.Error.WriteLineAsync($"Unknown command '{command}'. Usage: vsmcpdebugger [serve]").ConfigureAwait(false);
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);
LoggingSetup.Configure(builder.Logging, DiscoveryPaths.LogsDirectory);

builder.Services.AddSingleton<SessionState>();
builder.Services.AddSingleton<IConnectionManager>(sp =>
{
    var discovery = InstanceDiscovery.CreateDefault();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("VsMcpDebugger.Connection");
    return new ConnectionManager(
        discovery.List,
        (record, session, ct) => BridgeConnection.ConnectAsync(record, session, logger, ct),
        sp.GetRequiredService<SessionState>(),
        logger);
});

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "vsmcpdebugger", Version = typeof(SessionTools).Assembly.GetName().Version?.ToString() ?? "0.0.0" })
    .WithStdioServerTransport()
    .WithTools<SessionTools>()
    .WithTools<BreakpointTools>()
    .WithTools<ExecutionTools>()
    .WithTools<InspectionTools>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
