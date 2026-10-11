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

    public static string SetValue(string client, string expression, string value, string function) =>
        $"{ClientName(client)} wants to set {Visible(expression)} = {Visible(value)} in {Visible(function)}";

    public static string EvaluateWithSideEffects(string client, string expression, string function) =>
        $"{ClientName(client)} wants to evaluate {Visible(expression)} with side effects in {Visible(function)}";

    public static string Operation(string client, string tool, string summary) =>
        string.IsNullOrEmpty(summary)
            ? $"{ClientName(client)} wants to run {tool}"
            : $"{ClientName(client)} wants to run {tool}: {Visible(summary)}";

    public static string ClientName(string? name)
    {
        var visible = Visible(string.IsNullOrWhiteSpace(name) ? "AI agent" : name!.Trim());
        return visible.Length > MaxClientNameLength ? visible.Substring(0, MaxClientNameLength) + "…" : visible;
    }

    /// <summary>
    /// Escapes everything that is invisible or ambiguous on screen: control and format characters (incl. bidi
    /// overrides and tag characters outside the BMP), line/paragraph separators, spaces other than U+0020,
    /// private-use, unassigned and lone surrogate code points. Works on code points, not UTF-16 units.
    /// </summary>
    public static string Visible(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text!;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '\n': sb.Append("\\n"); continue;
                case '\r': sb.Append("\\r"); continue;
                case '\t': sb.Append("\\t"); continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                var pair = s.Substring(i, 2);
                if (IsHidden(CharUnicodeInfo.GetUnicodeCategory(pair, 0)))
                    sb.Append("\\U").Append(char.ConvertToUtf32(c, s[i + 1]).ToString("x8", CultureInfo.InvariantCulture));
                else
                    sb.Append(pair);
                i++;
                continue;
            }

            if (char.IsSurrogate(c) || IsHidden(CharUnicodeInfo.GetUnicodeCategory(c)) || (c != ' ' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator))
                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsHidden(UnicodeCategory category) => category is UnicodeCategory.Control or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse
        or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate;
}
