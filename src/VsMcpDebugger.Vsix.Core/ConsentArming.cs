using System;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// A consent question ignores Allow clicks for a short moment after it appears, so a click aimed at whatever was
/// under the pointer when the question popped up never counts as consent. Deny always counts.
/// </summary>
public static class ConsentArming
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    public static bool Accepts(ConsentChoice choice, TimeSpan sinceShown) => choice == ConsentChoice.Deny || sinceShown >= Delay;
}
