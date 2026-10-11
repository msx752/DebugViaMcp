using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VsDebug.Mcp.Connection;
using VsDebug.Mcp.Discovery;
using VsDebug.Mcp.Logging;
using VsDebug.Mcp.Session;
using VsDebug.Mcp.Tools;
using VsDebug.Protocol;

var command = args.FirstOrDefault() ?? "serve";
if (command is not "serve")
{
    await Console.Error.WriteLineAsync($"Unknown command '{command}'. Usage: vsdebug [serve]").ConfigureAwait(false);
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);
LoggingSetup.Configure(builder.Logging, DiscoveryPaths.LogsDirectory);

builder.Services.AddSingleton<SessionState>();
builder.Services.AddSingleton<IConnectionManager>(sp =>
{
    var discovery = InstanceDiscovery.CreateDefault();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("VsDebug.Connection");
    return new ConnectionManager(
        discovery.List,
        (record, session, ct) => BridgeConnection.ConnectAsync(record, session, logger, ct),
        sp.GetRequiredService<SessionState>());
});

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "vsdebug", Version = typeof(SessionTools).Assembly.GetName().Version?.ToString() ?? "0.0.0" })
    .WithStdioServerTransport()
    .WithTools<SessionTools>()
    .WithTools<BreakpointTools>()
    .WithTools<ExecutionTools>()
    .WithTools<InspectionTools>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
