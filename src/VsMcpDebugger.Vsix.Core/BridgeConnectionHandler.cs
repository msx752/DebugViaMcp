using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// One per pipe connection. Rejects everything until a handshake with the right token succeeds. Every later request
/// goes through <see cref="Guard{T}"/>: permission policy → consent → the call → audit.
/// </summary>
public sealed class BridgeConnectionHandler(DebugBridgeService service, string token, BridgeSecurity security, ILog log, ConnectionHooks? hooks = null)
    : IDebugBridge, IDisposable
{
    private readonly ConsentSession _consent = security.Consent.CreateSession();
    private volatile bool _authenticated;
    private volatile bool _revoked;
    private IDisposable? _registration;
    private readonly object _attachGate = new();
    private string _client = "";
    private string? _clientFolder;
    private volatile bool _approved;
    private readonly SemaphoreSlim _approvalGate = new(1, 1);
    private int _disposed;
    private readonly int _connectionId = hooks?.ConnectionId ?? 0;

    public async Task<HandshakeResponse> HandshakeAsync(HandshakeRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw Rpc(BridgeErrorCode.InvalidArgument, "Missing handshake request.", null);

        if (!TokenEquals(request.Token, token))
        {
            log.Warn("Handshake rejected: invalid token");
            throw Rpc(BridgeErrorCode.Unauthorized, "Invalid token.", null);
        }

        if (!security.Access.Enabled)
        {
            log.Warn("Handshake rejected: AI access is disconnected");
            throw Rpc(BridgeErrorCode.AiDisabled, "AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI.");
        }

        if (request.ProtocolMajor != ProtocolInfo.Major)
        {
            log.Warn($"Handshake rejected: protocol {request.ProtocolMajor}.x");
            throw Rpc(BridgeErrorCode.VersionMismatch, $"VSIX speaks protocol {ProtocolInfo.Major}.x.", "Update VsMcpDebugger.Mcp and the extension to the same release.");
        }

        HandshakeResponse response;
        try
        {
            response = await service.GetHandshakeInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeException e)
        {
            throw Rpc(e.Code, e.Message, e.Hint);
        }
#pragma warning disable CA1031 // Report as internal, never as an authentication failure.
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            log.Error("Handshake failed while reading Visual Studio state", e);
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the VsMcpDebugger output pane in Visual Studio.");
        }

        response.Permissions = security.Settings.Permissions;
        response.LogLevel = security.Settings.LogLevel;

        var first = false;
        lock (_attachGate)
        {
            // Two handshakes racing on one connection must register it once (M2 review minor 6).
            if (!_authenticated)
            {
                _client = ConsentText.ClientName(request.ClientName);
                _clientFolder = request.ClientWorkingDirectory;
                _approved = FolderMatch.Trusts(request.ClientWorkingDirectory, response.SolutionPath);
                _registration = security.Access.TryAttach(_client, Revoke)
                    ?? throw Rpc(BridgeErrorCode.AiDisabled, "AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI.");
                _authenticated = true;
                first = true;
            }
        }

        if (first)
        {
            hooks?.OnAuthenticated(request.ProtocolMinor);
            security.Audit.Record(new AuditEntry { Ts = DateTimeOffset.UtcNow, Client = _client, Operation = "connect", Outcome = "ok", Summary = _client });
        }

        log.Info($"Client '{_client}' connected");
        return response;
    }

    public Task<StatusInfo> GetStatusAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Status, context, async () =>
        {
            var status = await service.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            status.Permissions = security.Settings.Permissions;
            return status;
        }, null, null, cancellationToken);

    public Task<BreakpointInfo[]> ListBreakpointsAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.ListBreakpoints, context, () => service.ListBreakpointsAsync(cancellationToken), null, null, cancellationToken);

    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CallContext context, CancellationToken cancellationToken) =>
        Guard(spec is { Condition: not null } or { LogMessage: not null } ? Operation.SetBreakpointWithExpression : Operation.SetBreakpoint,
            context, () => service.SetBreakpointAsync(spec!, cancellationToken),
            Request.Of(Describe(spec), ("file", spec?.File), ("line", spec?.Line?.ToString(CultureInfo.InvariantCulture)), ("function", spec?.Function),
                ("condition", spec?.Condition), ("hitCount", spec is { HitCount: > 0 } ? spec.HitCount.ToString(CultureInfo.InvariantCulture) : null), ("logMessage", spec?.LogMessage)),
            spec is { Condition: not null } or { LogMessage: not null }
                ? async () =>
                {
                    await service.ValidateBreakpointAsync(spec, cancellationToken).ConfigureAwait(false);
                    return ConsentText.Breakpoint(_client, spec.Function ?? $"{spec.File}:{spec.Line?.ToString(CultureInfo.InvariantCulture)}", spec.Condition, spec.LogMessage);
                }
                : null,
            cancellationToken);

    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.RemoveBreakpoints, context, () => service.RemoveBreakpointsAsync(id, all, cancellationToken),
            Request.Of(all ? "all" : id ?? "", ("id", id), ("all", all ? "true" : null)), null, cancellationToken);

    public async Task<BreakpointInfo?> SetBreakpointEnabledAsync(string id, bool enabled, CallContext context, CancellationToken cancellationToken)
    {
        // The tier depends on the breakpoint: read it first. If its condition changes before the call, its id
        // changes too, so the call below matches nothing (fail closed).
        var target = _authenticated && !string.IsNullOrEmpty(id) ? await service.FindBreakpointAsync(id, cancellationToken).ConfigureAwait(false) : null;
        var runsCode = enabled && target is { Condition: not null } or { LogMessage: not null };
        var request = Request.Of($"{id} → {(enabled ? "on" : "off")}", ("id", id), ("enabled", enabled ? "true" : "false"));
        if (target is null && _authenticated)
            return await Guard(Operation.EnableBreakpoint, context, () => Task.FromResult<BreakpointInfo?>(null), request, null, cancellationToken).ConfigureAwait(false);

        return await Guard(runsCode ? Operation.EnableBreakpointWithExpression : Operation.EnableBreakpoint, context,
            () => service.SetBreakpointEnabledAsync(id, enabled, cancellationToken), request,
            runsCode
                ? () => Task.FromResult(ConsentText.EnableBreakpoint(_client, target!.Function ?? $"{target.File}:{target.Line?.ToString(CultureInfo.InvariantCulture)}", target.Condition, target.LogMessage))
                : null,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<long> StartAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Start, context, () => service.StartAsync(cancellationToken), null, null, cancellationToken);

    public Task<ProcessInfo[]> ListAttachableAsync(string? processName, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.ListProcesses, context, () => service.ListAttachableAsync(processName, cancellationToken),
            Request.Of(processName ?? "", ("processName", processName)), null, cancellationToken);

    public Task<long> AttachAsync(int pid, CallContext context, CancellationToken cancellationToken)
    {
        // Set when the question is prepared: the approval covers the process the question named.
        string? approvedName = null;
        return Guard(Operation.Attach, context, () => service.AttachAsync(pid, approvedName, cancellationToken),
            Request.Of($"pid {pid.ToString(CultureInfo.InvariantCulture)}", ("pid", pid.ToString(CultureInfo.InvariantCulture))),
            async () =>
            {
                approvedName = await service.DescribeAttachTargetAsync(pid, cancellationToken).ConfigureAwait(false);
                return ConsentText.Attach(_client, approvedName, pid);
            },
            cancellationToken);
    }

    public Task StopAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Stop, context, async () => { await service.StopAsync(cancellationToken).ConfigureAwait(false); return true; }, null, null, cancellationToken);

    public Task<long> ContinueAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Continue, context, () => service.ContinueAsync(cancellationToken), null, null, cancellationToken);

    public Task<RunToResult> RunToAsync(string file, int line, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.RunTo, context, () => service.RunToAsync(file, line, this, cancellationToken),
            Request.Of($"{file}:{line.ToString(CultureInfo.InvariantCulture)}", ("file", file), ("line", line.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    public Task<long> PauseAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Pause, context, () => service.PauseAsync(cancellationToken), null, null, cancellationToken);

    public Task<long> StepAsync(StepKind kind, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Step, context, () => service.StepAsync(kind, cancellationToken),
            Request.Of(KindName(kind), ("kind", KindName(kind))), null, cancellationToken);

    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Stack, context, () => service.GetStackAsync(maxFrames, cancellationToken), null, null, cancellationToken);

    public Task<ThreadInfo[]> GetThreadsAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Threads, context, () => service.GetThreadsAsync(cancellationToken), null, null, cancellationToken);

    public Task<StackInfo> GetThreadStackAsync(int? threadId, int maxFrames, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Stack, context, () => service.GetThreadStackAsync(threadId, maxFrames, cancellationToken),
            Request.Of(threadId?.ToString(CultureInfo.InvariantCulture) ?? "current", ("threadId", threadId?.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    public Task<FrameSelection> SelectFrameAsync(int index, long epoch, int? threadId, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.FrameSelect, context, () => service.SelectFrameAsync(index, epoch, threadId, cancellationToken),
            Request.Of($"frame {index.ToString(CultureInfo.InvariantCulture)}", ("index", index.ToString(CultureInfo.InvariantCulture)),
                ("epoch", epoch.ToString(CultureInfo.InvariantCulture)), ("threadId", threadId?.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    public Task<ExceptionInfo?> GetExceptionAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Exception, context, () => service.GetExceptionAsync(cancellationToken), null, null, cancellationToken);

    public Task<OutputPaneInfo[]> ListOutputPanesAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.OutputRead, context, () => service.ListOutputPanesAsync(cancellationToken), null, null, cancellationToken);

    public Task<OutputPage> ReadOutputAsync(string pane, int tail, int offset, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.OutputRead, context, () => service.ReadOutputAsync(pane, tail, offset, cancellationToken),
            Request.Of(pane ?? "", ("pane", pane), ("tail", tail.ToString(CultureInfo.InvariantCulture)), ("offset", offset.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    public Task<ErrorListPage> GetErrorListAsync(ErrorSeverity[] severities, int offset, int max, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.ErrorList, context, () => service.GetErrorListAsync(severities, offset, max, cancellationToken), null, null, cancellationToken);

    public Task<BuildResult> BuildAsync(string? project, int timeoutSec, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Build, context, () => service.BuildAsync(project, timeoutSec, cancellationToken),
            Request.Of(project ?? "solution", ("project", project), ("timeoutSec", timeoutSec.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    // Protocol 2.2 methods; later M3.1 tasks replace these stubs.
    public Task<long> StartProjectAsync(string project, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Start, context, () => service.StartProjectAsync(project, cancellationToken),
            Request.Of(project ?? "", ("project", project)), null, cancellationToken);

    public Task<BuildWarning[]> CheckBuildFreshnessAsync(string? project, string? file, CallContext context, CancellationToken cancellationToken) =>
        throw Rpc(BridgeErrorCode.Unsupported, "Not implemented yet.", null);

    public Task ReleaseAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Release, context, () => { ReleaseLease(); return Task.FromResult(true); }, null, null, cancellationToken);

    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Variables, context, () => service.GetVariablesAsync(scope, path, offset, maxItems, cancellationToken),
            Request.Of(path ?? ScopeName(scope), ("scope", ScopeName(scope)), ("path", path)), null, cancellationToken);

    public Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, CallContext context, CancellationToken cancellationToken)
    {
        if (!allowSideEffects)
            return Guard(Operation.Evaluate, context, () => service.EvaluateAsync(expression, false, cancellationToken),
                Request.Of(expression, ("expression", expression)), null, cancellationToken);

        // Set when the question is prepared: the approval covers the program state the question described.
        ConsentPoint? askedAt = null;
        return Guard(Operation.EvaluateWithSideEffects, context, () => service.EvaluateAsync(expression, true, askedAt, cancellationToken),
            Request.Of(expression, ("expression", expression)),
            async () =>
            {
                DebugBridgeService.ValidateSideEffectEvaluation(expression);
                var (function, point) = await service.GetBreakLocationAsync(cancellationToken).ConfigureAwait(false);
                askedAt = point;
                return ConsentText.EvaluateWithSideEffects(_client, expression, function);
            }, cancellationToken, succeeded: r => r.IsValid);
    }

    public Task<EvaluateResult> SetValueAsync(string expression, string value, CallContext context, CancellationToken cancellationToken)
    {
        // Set when the question is prepared: the approval covers the program state the question described.
        ConsentPoint? askedAt = null;
        return Guard(Operation.SetValue, context, () => service.SetValueAsync(expression, value, askedAt, cancellationToken),
            Request.Of($"{expression} = {value}", ("expression", expression), ("value", value)),
            async () =>
            {
                DebugBridgeService.ValidateSetValue(expression, value);
                var (function, point) = await service.GetBreakLocationAsync(cancellationToken).ConfigureAwait(false);
                askedAt = point;
                return ConsentText.SetValue(_client, expression, value, function);
            }, cancellationToken, succeeded: r => r.IsValid);
    }

    /// <summary>
    /// The settings decide first: a denied call never touches Visual Studio. When the user has to be asked,
    /// validation and the question are prepared first (<paramref name="prepare"/>), so a request that would fail
    /// anyway never bothers the user. Read calls are audited only when the user was asked.
    /// </summary>
    private async Task<T> Guard<T>(Operation operation, CallContext? context, Func<Task<T>> call, Request? request, Func<Task<string>>? prepare, CancellationToken ct,
        Func<T, bool>? succeeded = null)
    {
        if (!_authenticated) throw Rpc(BridgeErrorCode.Unauthorized, "Handshake required.", null);
        ThrowIfRevoked();
        request ??= Request.Empty;
        var callId = context?.CallId ?? "";
        var tier = PermissionPolicy.TierOf(operation);
        var started = Stopwatch.StartNew();
        var decision = AuditDecision.None;
        var prompted = false;

        void Audit(string outcome)
        {
            if (tier == PermissionTier.Read && !prompted) return;
            security.Audit.Record(new AuditEntry
            {
                Ts = DateTimeOffset.UtcNow, CallId = callId, Client = _client, Operation = PermissionPolicy.ToolName(operation),
                Tier = PermissionPolicy.TierName(tier), Params = request.Params, Decision = decision, Outcome = outcome,
                DurationMs = started.ElapsedMilliseconds, Summary = request.Summary,
            });
        }

        try
        {
            var level = PermissionPolicy.Decide(operation, security.Settings.Permissions);
            if (level == PermissionLevel.Deny)
            {
                decision = AuditDecision.DeniedPolicy;
                throw PolicyDenied(tier);
            }

            if (operation != Operation.Release) await EnsureApprovedAsync(ct).ConfigureAwait(false);
            switch (level)
            {
                case PermissionLevel.Ask:
                    EnsureLeaseAvailable(operation);
                    var text = prepare is null ? null : await prepare().ConfigureAwait(false);
                    ThrowIfRevoked();
                    prompted = true;
                    var outcome = await security.Consent.RequestAsync(_consent, operation,
                        text ?? ConsentText.Operation(_client, PermissionPolicy.ToolName(operation), request.Summary), ct,
                        () => PermissionPolicy.Decide(operation, security.Settings.Permissions)).ConfigureAwait(false);
                    decision = ToDecision(outcome);
                    ThrowIfDenied(outcome, tier);
                    break;
                default:
                    EnsureLeaseAvailable(operation);
                    decision = AuditDecision.Allow;
                    break;
            }

            ThrowIfRevoked();
            AcquireLease(operation);
            var result = await call().ConfigureAwait(false);
            Audit(succeeded is null || succeeded(result) ? "ok" : "failed");
            return result;
        }
        catch (BridgeException e)
        {
            Audit(BridgeErrorNames.ToName(e.Code));
            throw Rpc(e.Code, e.Message, e.Hint, e.Reason);
        }
        catch (LocalRpcException e)
        {
            Audit(BridgeErrorNames.ToName((BridgeErrorCode)e.ErrorCode));
            throw;
        }
        catch (OperationCanceledException)
        {
            // Spec §8: every control/mutate call leaves exactly one audit entry, cancelled ones included.
            if (decision == AuditDecision.None) decision = AuditDecision.DeniedCancelled;
            Audit("cancelled");
            throw;
        }
#pragma warning disable CA1031 // Every unexpected failure must reach the client as a coded error, not a dropped connection.
        catch (Exception e)
#pragma warning restore CA1031
        {
            log.Error($"[{callId}] Unexpected bridge failure", e);
            Audit("internal");
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the VsMcpDebugger output pane in Visual Studio.");
        }
    }

    /// <summary>
    /// An agent whose folder is not the solution's must be approved once by the owner, on its first call. Calls that
    /// arrive meanwhile wait for that single answer.
    /// </summary>
    private async Task EnsureApprovedAsync(CancellationToken ct)
    {
        if (_approved) return;
        await _approvalGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_approved) return;
            ThrowIfRevoked();
            // No policy shortcut here: Read = Allow must not skip the owner's approval of an unknown folder.
            var outcome = await security.Consent.RequestAsync(_consent, Operation.Connect, ConsentText.Connect(_client, _clientFolder), ct).ConfigureAwait(false);
            var decision = ToDecision(outcome);
            security.Audit.Record(new AuditEntry
            {
                Ts = DateTimeOffset.UtcNow, Client = _client, Operation = PermissionPolicy.ToolName(Operation.Connect),
                Tier = PermissionPolicy.TierName(PermissionTier.Read), Decision = decision,
                Outcome = outcome is ConsentOutcome.AllowedOnce or ConsentOutcome.AllowedForSession or ConsentOutcome.AllowedBySessionGrant ? "ok" : "denied",
                Summary = ConsentText.Connect(_client, _clientFolder),
            });
            ThrowIfDenied(outcome, PermissionTier.Read);
            _approved = true;
        }
        finally
        {
            _approvalGate.Release();
        }
    }

    /// <summary>Refuses before anything else happens (no question to the user, no call) while another agent holds this Visual Studio.</summary>
    private void EnsureLeaseAvailable(Operation operation)
    {
        if (LeaseRules.For(operation) == LeaseNeed.None) return;
        if (security.Lease.Holder is { } holder && holder.ConnectionId != _connectionId) throw InUse(holder);
    }

    /// <summary>
    /// The last check before the call, after any question to the user: a read re-checks the holder (the lease may have been
    /// taken while the question was open); the first state-changing command takes the lease, atomically, so of two
    /// simultaneous agents exactly one wins.
    /// </summary>
    private void AcquireLease(Operation operation)
    {
        if (LeaseRules.For(operation) == LeaseNeed.Read) EnsureLeaseAvailable(operation);
        if (LeaseRules.For(operation) != LeaseNeed.Change) return;
        var attempt = security.Lease.TryAcquire(_connectionId, _client, _clientFolder);
        if (attempt.Outcome == LeaseOutcome.HeldByOther) throw InUse(attempt.Holder!);
        if (attempt.Outcome == LeaseOutcome.Acquired) AuditLease("acquired");
    }

    private void ReleaseLease()
    {
        if (security.Lease.Release(_connectionId)) AuditLease("released");
    }

    private void AuditLease(string outcome) =>
        security.Audit.Record(new AuditEntry { Ts = DateTimeOffset.UtcNow, Client = _client, Operation = "lease", Outcome = outcome, Summary = outcome });

    private static LocalRpcException InUse(LeaseHolder holder) =>
        Rpc(BridgeErrorCode.InUse, ConsentText.InUse(holder.Client, holder.Folder),
            "Use another Visual Studio instance (vs_instances), or retry when the other agent has finished or released it.");

    /// <summary>Fail closed between Disconnect AI and the pipe actually closing; a revoked connection never comes back.</summary>
    private void ThrowIfRevoked()
    {
        if (_revoked || Volatile.Read(ref _disposed) == 1 || !security.Access.Enabled)
            throw Rpc(BridgeErrorCode.AiDisabled, "AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI, then reconnect.");
    }

    private static LocalRpcException PolicyDenied(PermissionTier tier) =>
        Rpc(BridgeErrorCode.Denied, $"Blocked by VsMcpDebugger settings in Visual Studio ({Capitalized(tier)} = Deny).",
            "Ask the user whether to change Tools → Options → VsMcpDebugger → Permissions.", DeniedReasons.Policy);

    private void ThrowIfDenied(ConsentOutcome outcome, PermissionTier tier)
    {
        switch (outcome)
        {
            case ConsentOutcome.DeniedByPolicy:
                throw PolicyDenied(tier);
            case ConsentOutcome.Denied:
                throw Rpc(BridgeErrorCode.Denied, "Denied by the user in Visual Studio.",
                    "This is the user's decision — do not retry; ask the user how to proceed.", DeniedReasons.UserDenied);
            case ConsentOutcome.TimedOut:
                var seconds = ((int)security.Settings.ConsentTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                throw Rpc(BridgeErrorCode.Denied, $"The user did not respond in Visual Studio within {seconds} s.",
                    "Ask the user to check Visual Studio, then retry if they agree.", DeniedReasons.Timeout);
            case ConsentOutcome.Cancelled:
                throw Rpc(BridgeErrorCode.Denied, "The request was cancelled before the user answered.", null, DeniedReasons.Cancelled);
            case ConsentOutcome.Disconnected:
                throw Rpc(BridgeErrorCode.Denied, "The user disconnected AI access in Visual Studio.",
                    "Ask the user to run Extensions → VsMcpDebugger → Reconnect AI.", DeniedReasons.Disconnected);
        }
    }

    private static string ToDecision(ConsentOutcome outcome) => outcome switch
    {
        ConsentOutcome.AllowedOnce => AuditDecision.AllowOnce,
        ConsentOutcome.AllowedByPolicy => AuditDecision.Allow,
        ConsentOutcome.DeniedByPolicy => AuditDecision.DeniedPolicy,
        ConsentOutcome.AllowedForSession or ConsentOutcome.AllowedBySessionGrant => AuditDecision.AllowSession,
        ConsentOutcome.Denied => AuditDecision.DeniedUser,
        ConsentOutcome.TimedOut => AuditDecision.DeniedTimeout,
        ConsentOutcome.Cancelled => AuditDecision.DeniedCancelled,
        _ => AuditDecision.DeniedDisconnected,
    };

    private static string Capitalized(PermissionTier tier) => tier switch
    {
        PermissionTier.Read => "Read",
        PermissionTier.Control => "Control",
        _ => "Mutate",
    };

    private static string KindName(StepKind kind) => kind switch
    {
        StepKind.Into => "into",
        StepKind.Out => "out",
        _ => "over",
    };

    private static string ScopeName(VariableScope scope) => scope == VariableScope.Args ? "args" : "locals";

    private static string Describe(BreakpointSpec? spec) =>
        spec is null ? ""
        : spec.Function ?? $"{spec.File}:{spec.Line?.ToString(CultureInfo.InvariantCulture)}" + (spec.Condition is null ? "" : $" when {spec.Condition}");

    /// <summary>Disconnect AI: pending consent ends as denied:disconnected, then the client is told and the pipe closes.</summary>
    private void Revoke()
    {
        _revoked = true;
        _consent.Revoke();
        ReleaseLease();
        if (hooks is null) return;
        _ = hooks.RevokeAndClose().ContinueWith(
            t => log.Warn($"Closing '{_client}' after Disconnect AI failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _consent.Dispose();
        _registration?.Dispose();
        ReleaseLease();
        _approvalGate.Dispose();
        _ = service.ReleaseRunToAsync(this).ContinueWith(
            t => log.Warn($"Removing the run-to breakpoint of '{_client}' failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        if (_authenticated)
            security.Audit.Record(new AuditEntry { Ts = DateTimeOffset.UtcNow, Client = _client, Operation = "disconnect", Outcome = "ok", Summary = _client });
    }

    private static LocalRpcException Rpc(BridgeErrorCode code, string message, string? hint, string? reason = null) =>
        new(message) { ErrorCode = (int)code, ErrorData = new BridgeErrorData { Hint = hint, Reason = reason } };

    /// <summary>Constant-time comparison (netstandard2.0 has no CryptographicOperations.FixedTimeEquals).</summary>
    private static bool TokenEquals(string? a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a ?? "");
        var y = Encoding.UTF8.GetBytes(b);
        var diff = x.Length ^ y.Length;
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++) diff |= x[i] ^ y[i];
        return diff == 0;
    }

    private sealed class Request
    {
        public static readonly Request Empty = new("", []);

        private Request(string summary, Dictionary<string, string?> parameters)
        {
            Summary = summary;
            Params = parameters;
        }

        public string Summary { get; }
        public Dictionary<string, string?> Params { get; }

        public static Request Of(string summary, params (string Name, string? Value)[] parameters)
        {
            var map = new Dictionary<string, string?>();
            foreach (var (name, value) in parameters)
            {
                if (value is not null) map[name] = value;
            }

            return new Request(summary, map);
        }
    }
}
