using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Logging;

/// <summary>
/// Logs go to stderr and to a rolling JSON-lines file. stdout is reserved for MCP traffic.
/// Level: DEBUGVIAMCP_LOG_LEVEL (Verbose|Debug|Information|Warning|Error), default Information.
/// </summary>
public static class LoggingSetup
{
    public static LogLevelControl Configure(ILoggingBuilder logging, string logsDirectory)
    {
        var overridden = Enum.TryParse<LogEventLevel>(Environment.GetEnvironmentVariable("DEBUGVIAMCP_LOG_LEVEL"), true, out var parsed);
        var levelSwitch = new LoggingLevelSwitch(overridden ? parsed : LogEventLevel.Information);

        Directory.CreateDirectory(logsDirectory);
        var serilog = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
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
        return new LogLevelControl(levelSwitch, overridden);
    }
}

/// <summary>The effective Mcp log level: DEBUGVIAMCP_LOG_LEVEL wins; otherwise Visual Studio's setting (sent at handshake).</summary>
public sealed class LogLevelControl(LoggingLevelSwitch levelSwitch, bool overridden)
{
    public LogEventLevel Current => levelSwitch.MinimumLevel;

    public void Apply(LogVerbosity fromIde)
    {
        if (overridden) return;
        levelSwitch.MinimumLevel = fromIde switch
        {
            LogVerbosity.Error => LogEventLevel.Error,
            LogVerbosity.Warning => LogEventLevel.Warning,
            LogVerbosity.Debug => LogEventLevel.Debug,
            _ => LogEventLevel.Information,
        };
    }
}
