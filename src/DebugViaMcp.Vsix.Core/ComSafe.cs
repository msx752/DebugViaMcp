using System;
using System.Runtime.InteropServices;

namespace DebugViaMcp.Vsix.Core;

/// <summary>
/// Visual Studio's automation objects fail on individual properties (a pane without a text document, a thread without
/// a location, a released object). One unreadable best-effort field must make that field unavailable, never fail the
/// whole call. Identity fields and commands do not use this: their failures are real.
/// </summary>
public static class ComSafe
{
    /// <returns>False when Visual Studio could not give the value; <paramref name="value"/> is then the default.</returns>
    public static bool TryRead<T>(Func<T> read, out T value)
    {
        if (read is null) throw new ArgumentNullException(nameof(read));
        try
        {
            value = read();
            return true;
        }
        catch (COMException)
        {
        }
        catch (InvalidComObjectException)
        {
        }

        value = default!;
        return false;
    }
}
