using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DebugViaMcp.Vsix.Core.Security;

/// <summary>A debugger value after the mask: what the agent gets, and whether anything was hidden.</summary>
public readonly struct MaskedValue
{
    public MaskedValue(string value, bool masked)
    {
        Value = value;
        Masked = masked;
    }

    public string Value { get; }
    public bool Masked { get; }
}

/// <summary>
/// Hides values that look like secrets before they leave Visual Studio towards the AI agent: by the name they sit under
/// (a string named Password) and by their shape (a JWT, a PEM private key, Password=… in a connection string).
/// A heuristic that lowers the chance of a leak, not a guarantee. Visual Studio itself still shows the real value.
/// </summary>
public static class SensitiveValueMask
{
    public const string Placeholder = "[masked]";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly Regex UnicodeEscape = new(@"\\u([0-9A-Fa-f]{4})|\\U([0-9A-Fa-f]{8})", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex Words = new("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly HashSet<string> SensitiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "pwd", "passphrase", "secret", "secrets", "token", "tokens", "apikey", "credential", "credentials",
        "authorization", "bearer", "privatekey", "connectionstring",
    };

    private static readonly (string First, string Second)[] SensitivePairs =
    [
        ("api", "key"), ("access", "key"), ("private", "key"), ("secret", "key"), ("signing", "key"), ("connection", "string"),
    ];

    /// <summary>Secret-shaped pieces replaced inline; the pattern's own text goes (Bearer …), a captured value part goes (Password=…).</summary>
    private static readonly Regex[] WholeMatches =
    [
        new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----(?:[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----|[\s\S]*$)", Options, MatchTimeout),
        new(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}", Options & ~RegexOptions.IgnoreCase, MatchTimeout),
        new(@"\bBearer\s+[A-Za-z0-9\-._~+/]{16,}=*", Options, MatchTimeout),
        new(@"\bgh[pousr]_[A-Za-z0-9]{30,}\b", Options & ~RegexOptions.IgnoreCase, MatchTimeout),
        new(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", Options & ~RegexOptions.IgnoreCase, MatchTimeout),
    ];

    private static readonly Regex KeyValueSecret = new(
        @"(?<key>\b(?:password|passwd|pwd|secret|client_secret|accountkey|sharedaccesskey|sharedaccesssignature|apikey|api_key|access_key|token)\s*=\s*)(?<value>[^;""'&\s,]+)",
        Options, MatchTimeout);

    private static readonly Regex UrlPassword = new(@"(?<=\b[a-z][a-z0-9+.\-]*://[^/\s:@]+:)[^@\s/]+(?=@)", Options, MatchTimeout);

    /// <summary>For a named value (a variable, a member, an evaluated expression). <paramref name="type"/> is the debugger's type name, when known.</summary>
    public static MaskedValue MaskValue(string? name, string? value, string? type = null)
    {
        if (string.IsNullOrEmpty(value)) return new MaskedValue("", false);

        if (IsSensitiveName(name) && IsReadableSecret(name!, value!, type)) return new MaskedValue(Placeholder, true);

        var text = MaskText(value, out var masked);
        if (masked && text.Trim('"') == Placeholder) text = Placeholder;
        return new MaskedValue(text, masked);
    }

    /// <summary>Whether an expression or a path names something sensitive (a word of it does): used to refuse a tracepoint that would print it.</summary>
    public static bool NamesSensitiveValue(string? expression) => IsSensitiveName(expression);

    /// <summary>For free text (an exception message, a stack trace, an Output line): shapes only, there is no name to judge.</summary>
    public static string MaskText(string? text, out bool masked)
    {
        masked = false;
        if (string.IsNullOrEmpty(text)) return "";

        var current = text!;
        try
        {
            foreach (var pattern in WholeMatches)
                current = Replace(pattern, current, _ => Placeholder, ref masked);
            current = Replace(KeyValueSecret, current, m => m.Groups["key"].Value + Placeholder, ref masked);
            current = Replace(UrlPassword, current, _ => Placeholder, ref masked);
            return current;
        }
        catch (RegexMatchTimeoutException)
        {
            // A value that stalls the matcher is not shown: hiding too much is the safe side.
            masked = true;
            return Placeholder;
        }
    }

    private static string Replace(Regex pattern, string input, MatchEvaluator evaluator, ref bool masked)
    {
        var any = false;
        var result = pattern.Replace(input, m => { any = true; return evaluator(m); });
        if (any) masked = true;
        return result;
    }

    /// <summary>
    /// What a sensitive name is worth hiding: a string or a char (quoted or not: a format specifier such as <c>, nq</c> removes the quotes),
    /// or any scalar read out of an element (<c>password[0]</c>). Numbers, flags, null and objects are not secrets to hide.
    /// </summary>
    private static bool IsReadableSecret(string name, string value, string? type)
    {
        var v = value.Trim();
        if (v == "null" || v == "Nothing") return false;

        // The declared type decides first: with a format specifier such as nq the quotes are gone and a secret may start with a brace.
        if (type is not null && (type.EndsWith("string", StringComparison.OrdinalIgnoreCase) || type.EndsWith("char", StringComparison.OrdinalIgnoreCase))) return true;
        if (IsQuotedString(v) || v.StartsWith("'", StringComparison.Ordinal)) return true;
        if (v.StartsWith("{", StringComparison.Ordinal)) return false;

        // An expression built on the secret (hash, comparison, prefix test) is an oracle for it, whatever type it returns.
        if (IsDerived(name) && !IsLengthOrCount(name)) return true;
        return name.IndexOf('[') >= 0;
    }

    private const string OperatorCharacters = "=<>!&|?+-*/%^~";

    private static bool IsDerived(string expression) => expression.IndexOf('(') >= 0 || expression.IndexOfAny(OperatorCharacters.ToCharArray()) >= 0;

    /// <summary>How long or how many says little about the secret; with an operator in the expression it could say more (a comparison).</summary>
    private static bool IsLengthOrCount(string expression)
    {
        var e = expression.Trim();
        return (e.EndsWith(".Length", StringComparison.Ordinal) || e.EndsWith(".Count", StringComparison.Ordinal)) && e.IndexOfAny(OperatorCharacters.ToCharArray()) < 0;
    }

    private static bool IsQuotedString(string value)
    {
        var v = value.TrimStart();
        return v.StartsWith("\"", StringComparison.Ordinal) || v.StartsWith("@\"", StringComparison.Ordinal) || v.StartsWith("$\"", StringComparison.Ordinal);
    }

    /// <summary>\uXXXX and \UXXXXXXXX; an escape that is not a character (a lone surrogate, beyond U+10FFFF) is left as it is.</summary>
    private static string DecodeEscape(Match m)
    {
        var code = Convert.ToInt64(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, 16);
        return code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF) ? char.ConvertFromUtf32((int)code) : m.Value;
    }

    private static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // C# allows unicode escapes inside an identifier (pass\u0077ord): read the name the way the debugger will.
        name = UnicodeEscape.Replace(name!, DecodeEscape);

        var words = new List<string>();
        foreach (Match m in Words.Matches(name!)) words.Add(m.Value.ToLowerInvariant());

        foreach (var word in words)
            if (SensitiveWords.Contains(word)) return true;

        for (var i = 0; i + 1 < words.Count; i++)
            foreach (var (first, second) in SensitivePairs)
                if (words[i] == first && words[i + 1] == second) return true;

        return false;
    }
}
