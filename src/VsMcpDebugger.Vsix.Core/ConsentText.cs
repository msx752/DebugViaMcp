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

    public static string Visible(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text!.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    var category = CharUnicodeInfo.GetUnicodeCategory(c);
                    if (char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
