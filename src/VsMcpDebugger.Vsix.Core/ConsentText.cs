using System.Globalization;
using System.Text;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Wording of consent questions. Everything the agent controls is shown in full and with control and format
/// characters (line breaks, bidi overrides, zero-width marks) escaped, so the user never approves hidden text.
/// </summary>
public static class ConsentText
{
    public const int MaxClientNameLength = 64;
    public const int MaxFolderLength = 120;

    // Every builder: <paramref name="client"/> is the name ClientName returned at handshake (already escaped and capped).

    public static string SetValue(string client, string expression, string value, string function) =>
        $"{client} wants to set {Visible(expression)} = {Visible(value)} in {Visible(function)}";

    public static string EvaluateWithSideEffects(string client, string expression, string function) =>
        $"{client} wants to evaluate {Visible(expression)} with side effects in {Visible(function)}";

    public static string Breakpoint(string client, string location, string? condition, string? logMessage)
    {
        var what = condition is not null && logMessage is not null ? $"when {Visible(condition)}, logging \"{Visible(logMessage)}\""
            : condition is not null ? $"when {Visible(condition)}"
            : $"logging \"{Visible(logMessage)}\"";
        return $"{client} wants to set a breakpoint at {Visible(location)} {what} — this runs in your program on every hit";
    }

    public static string EnableBreakpoint(string client, string location, string? condition, string? logMessage) =>
        Breakpoint(client, location, condition, logMessage).Replace(" wants to set a breakpoint at ", " wants to enable the breakpoint at ");

    public static string Attach(string client, string process, int pid) =>
        $"{client} wants to attach the debugger to {Visible(process)} (pid {pid.ToString(CultureInfo.InvariantCulture)}) — Visual Studio will be able to read and change that process's memory";

    public static string Operation(string client, string tool, string summary) =>
        string.IsNullOrEmpty(summary)
            ? $"{client} wants to run {tool}"
            : $"{client} wants to run {tool}: {Visible(summary)}";

    /// <summary>Refusal shown to an agent while another one holds this Visual Studio; <paramref name="holderClient"/> is already escaped.</summary>
    public static string InUse(string holderClient, string? holderFolder) =>
        string.IsNullOrEmpty(holderFolder)
            ? $"This Visual Studio is in use by {holderClient}."
            : $"This Visual Studio is in use by {holderClient} (working in {CappedFolder(holderFolder)}).";

    /// <summary>The folder an agent reported, escaped and cut to <see cref="MaxFolderLength"/> characters.</summary>
    public static string CappedFolder(string? folder)
    {
        var visible = Visible(folder);
        if (visible.Length <= MaxFolderLength) return visible;
        var cut = char.IsHighSurrogate(visible[MaxFolderLength - 1]) ? MaxFolderLength - 1 : MaxFolderLength;
        return visible.Substring(0, cut) + "…";
    }

    public static string ClientName(string? name)
    {
        var visible = Visible(string.IsNullOrWhiteSpace(name) ? "AI agent" : name!.Trim());
        if (visible.Length <= MaxClientNameLength) return visible;
        var cut = char.IsHighSurrogate(visible[MaxClientNameLength - 1]) ? MaxClientNameLength - 1 : MaxClientNameLength;
        return visible.Substring(0, cut) + "…";
    }

    /// <summary>
    /// Escapes everything that is invisible or ambiguous on screen: control and format characters (incl. bidi
    /// overrides and tag characters outside the BMP), line/paragraph separators, spaces other than U+0020,
    /// private-use, unassigned and lone surrogate code points, and every combining mark that does not sit directly
    /// on a visible base character (a stray or stacked mark would decorate the text around it). A literal backslash
    /// is doubled, so an escape shown to the user can only come from this method. Works on code points.
    /// </summary>
    public static string Visible(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text!;
        var sb = new StringBuilder(s.Length);
        var afterVisibleBase = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); afterVisibleBase = false; continue;
                case '\n': sb.Append("\\n"); afterVisibleBase = false; continue;
                case '\r': sb.Append("\\r"); afterVisibleBase = false; continue;
                case '\t': sb.Append("\\t"); afterVisibleBase = false; continue;
            }

            var isPair = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            var unit = isPair ? s.Substring(i, 2) : c.ToString();
            var category = CharUnicodeInfo.GetUnicodeCategory(unit, 0);
            var escape = (!isPair && char.IsSurrogate(c)) || IsHidden(category)
                || (category == UnicodeCategory.SpaceSeparator && c != ' ')
                || (IsMark(category) && !afterVisibleBase);

            if (escape)
            {
                if (isPair)
                    sb.Append("\\U").Append(char.ConvertToUtf32(c, s[i + 1]).ToString("x8", CultureInfo.InvariantCulture));
                else
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                afterVisibleBase = false;
            }
            else
            {
                sb.Append(unit);
                afterVisibleBase = !IsMark(category) && c != ' ';
            }

            if (isPair) i++;
        }

        return sb.ToString();
    }

    private static bool IsMark(UnicodeCategory category) =>
        category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsHidden(UnicodeCategory category) => category is UnicodeCategory.Control or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse
        or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate;
}
