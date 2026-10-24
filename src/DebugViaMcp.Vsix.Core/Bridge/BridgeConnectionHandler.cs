using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Instances;
using DebugViaMcp.Vsix.Core.Security;

namespace DebugViaMcp.Vsix.Core.Bridge;

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
            throw Rpc(BridgeErrorCode.AiDisabled, "AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → Debugging via MCP → Reconnect AI.");
        }

        if (request.ProtocolMajor != ProtocolInfo.Major)
        {
            log.Warn($"Handshake rejected: protocol {request.ProtocolMajor}.x");
            throw Rpc(BridgeErrorCode.VersionMismatch, $"VSIX speaks protocol {ProtocolInfo.Major}.x.", "Update DebugViaMcp.Mcp and the extension to the same release.");
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
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the Debugging via MCP output pane in Visual Studio.");
        }

        response.Permissions = security.Settings.Permissions;
        response.LogLevel = security.Settings.LogLevel;
        response.LogRetentionDays = security.Settings.LogRetentionDays;
        response.MaxWaitSeconds = security.Settings.MaxWaitSeconds;
        response.MaxBuildSeconds = security.Settings.MaxBuildSeconds;

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
                    ?? throw Rpc(BridgeErrorCode.AiDisabled, "AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → Debugging via MCP → Reconnect AI.");
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
            status.LeaseHolder = security.Lease.Holder?.Client;
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

    public Task<SymbolStatus> GetSymbolStatusAsync(CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.SymbolStatus, context, () => service.GetSymbolStatusAsync(cancellationToken), null, null, cancellationToken);

    public Task<ExceptionSettingsPage> ListExceptionSettingsAsync(string? group, string? filter, bool onlyEnabled, int limit, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.ListExceptionSettings, context, () => service.ListExceptionSettingsAsync(group, filter, onlyEnabled, limit, cancellationToken),
            Request.Of(group ?? "all groups", ("group", group), ("filter", filter), ("onlyEnabled", onlyEnabled ? "true" : "false"), ("limit", limit.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    public Task<ExceptionSetResult> SetExceptionSettingsAsync(string group, string[] names, bool breakWhenThrown, CallContext context, CancellationToken cancellationToken)
    {
        var joined = string.Join(", ", names ?? []);
        return Guard(Operation.SetExceptionSettings, context, () => service.SetExceptionSettingsAsync(this, group, names!, breakWhenThrown, cancellationToken),
            Request.Of($"{group}: {joined} → {(breakWhenThrown ? "on" : "off")}", ("group", group), ("names", joined), ("breakWhenThrown", breakWhenThrown ? "true" : "false")),
            async () =>
            {
                // Validates first, so a request that would fail anyway never asks. Lists what changes; when nothing does, what was named.
                var plan = await service.PlanExceptionSettingsAsync(group, names!, breakWhenThrown, cancellationToken).ConfigureAwait(false);
                var changing = plan.Changes.Where(c => c.Changed).Select(c => c.Name).ToList();
                return ConsentText.ExceptionSettings(_client, plan.Group, changing.Count > 0 ? changing : plan.Changes.Select(c => c.Name).ToList(), breakWhenThrown);
            },
            cancellationToken);
    }

    public async Task<ExceptionResetResult> ResetExceptionSettingsAsync(CallContext context, CancellationToken cancellationToken)
    {
        // Nothing recorded: the call only says so, which is a read. It asks no question and takes no lease, so a cleanup call
        // of an agent that changed nothing never locks the other agents out.
        var nothing = _authenticated && await service.PendingExceptionChangesAsync(this).ConfigureAwait(false) == 0;
        return await Guard(nothing ? Operation.ListExceptionSettings : Operation.ResetExceptionSettings, context,
            () => service.ResetExceptionSettingsAsync(this, cancellationToken), null,
            nothing ? null : async () => ConsentText.ExceptionReset(_client, await service.PendingExceptionChangesAsync(this).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ValuePage> ReadValuePageAsync(string expression, int offset, int length, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Evaluate, context, () => service.ReadValuePageAsync(expression, offset, length, cancellationToken),
            Request.Of(expression, ("expression", expression), ("offset", offset.ToString(CultureInfo.InvariantCulture)), ("length", length.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    public Task<AddWatchResult> AddWatchAsync(string expression, string? name, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.AddWatch, context, () => service.AddWatchAsync(this, expression, name, cancellationToken),
            Request.Of(expression, ("expression", expression), ("name", name)), null, cancellationToken);

    public Task<WatchListResult> ListWatchesAsync(bool atStop, CallContext context, CancellationToken cancellationToken) =>
        Guard(atStop ? Operation.ReadWatchValues : Operation.ListWatches, context, () => service.ListWatchesAsync(this, cancellationToken), null, null, cancellationToken);

    public Task<RemoveWatchResult> RemoveWatchAsync(string? id, bool all, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.RemoveWatch, context, () => service.RemoveWatchAsync(this, id, all, cancellationToken),
            Request.Of(all ? "all" : id ?? "", ("id", id), ("all", all ? "true" : "false")), null, cancellationToken);

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
        // The user's Options cap how long an agent may keep the build waiting, whatever the agent asked for.
        Guard(Operation.Build, context, () => service.BuildAsync(project, Math.Min(Math.Max(1, timeoutSec), security.Settings.MaxBuildSeconds), cancellationToken),
            Request.Of(project ?? "solution", ("project", project), ("timeoutSec", timeoutSec.ToString(CultureInfo.InvariantCulture))), null, cancellationToken);

    // Protocol 2.2 methods; later M3.1 tasks replace these stubs.
    public Task<long> StartProjectAsync(string project, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.Start, context, () => service.StartProjectAsync(project, cancellationToken),
            Request.Of(project ?? "", ("project", project)), null, cancellationToken);

    public Task<BuildWarning[]> CheckBuildFreshnessAsync(string? project, string? file, CallContext context, CancellationToken cancellationToken) =>
        Guard(Operation.BuildCheck, context, () => service.CheckBuildFreshnessAsync(project, file, cancellationToken),
            Request.Of(project ?? file ?? "startup project", ("project", project), ("file", file)), null, cancellationToken);

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
            // Giving up a hold can never hurt, so no setting may block it.
            var level = operation == Operation.Release ? PermissionLevel.Allow : PermissionPolicy.Decide(operation, security.Settings.Permissions);
            if (level == PermissionLevel.Deny)
            {
                decision = AuditDecision.DeniedPolicy;
                throw PolicyDenied(tier);
            }

            if (operation != Operation.Release) await EnsureApprovedAsync(callId, ct).ConfigureAwait(false);

            // A dialog Visual Studio waits on cannot be answered through the tools (and an info bar in its blocked window cannot be answered
            // either): say so now instead of letting the call hang. After the approval, so an unapproved agent learns nothing; no await,
            // so the lease check below stays atomic with taking the lease.
            if (tier != PermissionTier.Read && service.GetBlockingDialog() is { } dialog)
            {
                log.Warn($"[{callId}] {PermissionPolicy.ToolName(operation)} not run: Visual Studio waits on the dialog \"{dialog}\"");
                throw Rpc(BridgeErrorCode.Busy, $"Visual Studio is waiting for an answer to a dialog: \"{dialog}\".", "Answer it in Visual Studio (the agent does not click dialogs), then call again.");
            }

            switch (level)
            {
                case PermissionLevel.Ask:
                    EnsureLeaseAvailable(operation);
                    var text = prepare is null ? null : await prepare().ConfigureAwait(false);
                    ThrowIfRevoked();
                    prompted = true;
                    var outcome = await security.Consent.RequestAsync(_consent, operation,
                        text ?? ConsentText.Operation(_client, PermissionPolicy.ToolName(operation), request.Summary), ct,
                        () => PermissionPolicy.Decide(operation, security.Settings.Permissions), () => !LeaseTakenByOther(operation),
                        // Take the lease before the gate lets the next queued question in, or another agent's question could be shown
                        // in between although this agent is about to hold the Visual Studio.
                        allowed =>
                        {
                            decision = ToDecision(allowed);
                            ThrowIfRevoked();
                            AcquireLease(operation);
                        }).ConfigureAwait(false);
                    decision = ToDecision(outcome);
                    if (outcome == ConsentOutcome.Cancelled) EnsureLeaseAvailable(operation);
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
            throw Rpc(BridgeErrorCode.Internal, e.Message, "See the Debugging via MCP output pane in Visual Studio.");
        }
    }

    /// <summary>
    /// An agent whose folder is not the solution's must be approved once by the owner, on its first call. Calls that
    /// arrive meanwhile wait for that single answer.
    /// </summary>
    private async Task EnsureApprovedAsync(string callId, CancellationToken ct)
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
                Ts = DateTimeOffset.UtcNow, CallId = callId, Client = _client, Operation = PermissionPolicy.ToolName(Operation.Connect),
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

    private bool LeaseTakenByOther(Operation operation) =>
        LeaseRules.For(operation) != LeaseNeed.None && security.Lease.Holder is { } holder && holder.ConnectionId != _connectionId;

    /// <summary>
    /// The last check before the call, after any question to the user: a read re-checks the holder (the lease may have been
    /// taken while the question was open); the first state-changing command takes the lease, atomically, so of two
    /// simultaneous agents exactly one wins.
    /// </summary>
    private void AcquireLease(Operation operation)
    {
        if (LeaseRules.For(operation) == LeaseNeed.Read) EnsureLeaseAvailable(operation);
        if (LeaseRules.For(operation) != LeaseNeed.Change) return;
        var attempt = security.Lease.TryAcquire(_connectionId, _client, string.IsNullOrEmpty(_clientFolder) ? null : ConsentText.CappedFolder(_clientFolder));
        if (attempt.Outcome == LeaseOutcome.HeldByOther) throw InUse(attempt.Holder!);
        if (attempt.Outcome == LeaseOutcome.Acquired)
        {
            AuditLease("acquired");
            // The connection may have been closed or revoked between the check above and the take: never keep a lease for a dead connection.
            if (_revoked || Volatile.Read(ref _disposed) == 1)
            {
                ReleaseLease();
                ThrowIfRevoked();
            }
        }
    }

    private void ReleaseLease()
    {
        // The agent's watch list ends with its hold, whether or not it ever took the lease (a watch needs none).
        _ = service.ReleaseWatchesAsync(this).ContinueWith(
            t => log.Warn($"Dropping the watch list of '{_client}' failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        if (!security.Lease.Release(_connectionId)) return;
        AuditLease("released");
        // The agent can no longer call exception_reset: put its Exception Settings changes back now.
        RestoreExceptionSettings();
    }

    private void RestoreExceptionSettings() =>
        _ = service.ReleaseExceptionSettingsAsync(this).ContinueWith(
            t => log.Warn($"Putting back the Exception Settings changed by '{_client}' failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private void AuditLease(string outcome) =>
        security.Audit.Record(new AuditEntry { Ts = DateTimeOffset.UtcNow, Client = _client, Operation = "lease", Outcome = outcome, Summary = outcome });

    private static LocalRpcException InUse(LeaseHolder holder) =>
        Rpc(BridgeErrorCode.InUse, ConsentText.InUse(holder.Client, holder.Folder),
            "Use another Visual Studio instance (vs_instances), or retry when the other agent has finished or released it.");

    /// <summary>Fail closed between Disconnect AI and the pipe actually closing; a revoked connection never comes back.</summary>
    private void ThrowIfRevoked()
    {
        if (_revoked || Volatile.Read(ref _disposed) == 1 || !security.Access.Enabled)
            throw Rpc(BridgeErrorCode.AiDisabled, "AI access is disconnected in Visual Studio.", "Ask the user to run Extensions → Debugging via MCP → Reconnect AI, then reconnect.");
    }

    private static LocalRpcException PolicyDenied(PermissionTier tier) =>
        Rpc(BridgeErrorCode.Denied, $"Blocked by Debugging via MCP settings in Visual Studio ({Capitalized(tier)} = Deny).",
            "Ask the user whether to change Tools → Options → Debugging → Debugging via MCP (Permissions).", DeniedReasons.Policy);

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
                    "Ask the user to run Extensions → Debugging via MCP → Reconnect AI.", DeniedReasons.Disconnected);
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
        RestoreExceptionSettings();
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
