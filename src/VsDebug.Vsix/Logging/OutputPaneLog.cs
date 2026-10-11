using System;
using System.Globalization;
using Microsoft.VisualStudio.Shell.Interop;
using VsDebug.Vsix.Core;

namespace VsDebug.Vsix.Logging;

/// <summary>Writes to View → Output → "VsDebug". OutputStringThreadSafe may be called from any thread.</summary>
internal sealed class OutputPaneLog(IVsOutputWindowPane pane) : ILog
{
    public void Info(string message) => Write("INF", message);
    public void Warn(string message) => Write("WRN", message);
    public void Error(string message, Exception? exception = null) => Write("ERR", message + (exception is null ? "" : " | " + exception.Message));

    private void Write(string level, string message)
    {
#pragma warning disable VSTHRD010 // OutputStringThreadSafe is documented as callable from any thread.
        pane.OutputStringThreadSafe($"{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}");
#pragma warning restore VSTHRD010
    }
}
