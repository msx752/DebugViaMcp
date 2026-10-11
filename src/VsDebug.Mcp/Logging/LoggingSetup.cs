using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace VsDebug.Mcp.Logging;

/// <summary>
/// Logs go to stderr and to a rolling JSON-lines file. stdout is reserved for MCP traffic.
/// Level: VSDEBUG_LOG_LEVEL (Verbose|Debug|Information|Warning|Error), default Information.
/// </summary>
public static class LoggingSetup
{
    public static void Configure(ILoggingBuilder logging, string logsDirectory)
    {
        var level = Enum.TryParse<LogEventLevel>(Environment.GetEnvironmentVariable("VSDEBUG_LOG_LEVEL"), true, out var parsed)
            ? parsed
            : LogEventLevel.Information;

        Directory.CreateDirectory(logsDirectory);
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture, standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(logsDirectory, "mcp-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 7)
            .CreateLogger();

        logging.ClearProviders();
        logging.AddSerilog(serilog, dispose: true);
    }
}
