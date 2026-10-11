using System;
using System.Globalization;
using System.IO;
using VsDebug.Vsix.Core;

namespace VsDebug.Vsix.Logging;

/// <summary>Plain-text log at &lt;VsDebug home&gt;\logs\vsix-{pid}-{date}.log. Never receives variable values.</summary>
internal sealed class FileLog(string directory, int pid) : ILog
{
    private readonly object _gate = new();

    public void Info(string message) => Write("INF", message, null);
    public void Warn(string message) => Write("WRN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var now = DateTime.UtcNow;
            var path = Path.Combine(directory, $"vsix-{pid.ToString(CultureInfo.InvariantCulture)}-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
            var line = $"{now.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{(exception is null ? "" : " | " + exception)}{Environment.NewLine}";
            lock (_gate) File.AppendAllText(path, line);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
