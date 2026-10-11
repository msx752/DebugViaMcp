using System;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Options;

/// <summary>Reads the page on every call (plain fields, safe off the UI thread), so changes apply to the next request.</summary>
internal sealed class OptionsBridgeSettings(GeneralOptionsPage page) : IBridgeSettings
{
    public PermissionSet Permissions => new() { Read = page.Read, Control = page.Control, Mutate = page.Mutate };
    public TimeSpan ConsentTimeout => TimeSpan.FromSeconds(page.ConsentTimeoutSeconds);
    public LogVerbosity LogLevel => page.LogLevel;
}
