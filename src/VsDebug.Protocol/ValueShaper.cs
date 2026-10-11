namespace VsDebug.Protocol;

public static class ValueShaper
{
    public const int MaxValueLength = 1024;

    /// <summary>Caps a debugger value so a single variable cannot flood the agent's context.</summary>
    public static (string Value, bool Truncated) Truncate(string? value, int max = MaxValueLength)
    {
        if (value is null) return ("", false);
        if (value.Length <= max) return (value, false);
        return (value.Substring(0, max - 1) + "…", true);
    }
}
