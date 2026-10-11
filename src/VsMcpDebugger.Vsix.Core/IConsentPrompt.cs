using System;
using System.Threading;
using System.Threading.Tasks;

namespace VsMcpDebugger.Vsix.Core;

public enum ConsentChoice { AllowOnce, AllowSession, Deny }

/// <summary>Asks the user in the IDE. Cancelling <c>ct</c> must dismiss the question and throw OperationCanceledException.</summary>
public interface IConsentPrompt
{
    Task<ConsentChoice> AskAsync(string text, TimeSpan timeout, CancellationToken ct);
}
