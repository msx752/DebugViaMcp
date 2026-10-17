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
var levels = LoggingSetup.Configure(builder.Logging, DiscoveryPaths.LogsDirectory);
var identity = new ClientIdentity();

builder.Services.AddSingleton<SessionState>();
builder.Services.AddSingleton<IConnectionManager>(sp =>
{
    var discovery = InstanceDiscovery.CreateDefault();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("VsMcpDebugger.Connection");
    return new ConnectionManager(
        discovery.List,
        async (record, session, ct) =>
        {
            var connection = await BridgeConnection.ConnectAsync(record, session, logger, ct, clientName: identity.Name).ConfigureAwait(false);
            levels.Apply(connection.Handshake.LogLevel);
            // The MCP server's own log files follow the same retention as the extension's (Tools > Options > VsMcpDebugger > Logging).
            if (connection.Handshake.LogRetentionDays > 0) LogRetention.Prune(DiscoveryPaths.LogsDirectory, "mcp-", connection.Handshake.LogRetentionDays, DateTimeOffset.UtcNow);
            return connection;
        },
        sp.GetRequiredService<SessionState>(),
        logger);
});

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "vsmcpdebugger", Version = typeof(SessionTools).Assembly.GetName().Version?.ToString() ?? "0.0.0" })
    .WithStdioServerTransport()
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, ct) =>
    {
        // The MCP client's name (e.g. claude-code) is what Visual Studio shows in consent questions.
        identity.Capture(context.Server.ClientInfo?.Name);
        return await next(context, ct).ConfigureAwait(false);
    }))
    .WithTools<SessionTools>()
    .WithTools<BreakpointTools>()
    .WithTools<ExecutionTools>()
    .WithTools<InspectionTools>()
    .WithTools<IdeTools>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
