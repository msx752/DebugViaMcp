using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.RpcContracts.UnifiedSettings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Options;

/// <summary>
/// The values of Tools → Options → All Settings → Debugging → Debugging via MCP (registered in UnifiedSettings\registration.json).
/// Visual Studio's settings experience shows and stores them; this class reads them and follows every change, so a change
/// takes effect on the next request without a restart.
/// </summary>
internal sealed class McpSettings : IObserver<SettingsUpdate>, IDisposable
{
    private const string Prefix = "debugging.mcp.";
    private const string Read = Prefix + "read", Control = Prefix + "control", Mutate = Prefix + "mutate";
    private const string ConsentTimeout = Prefix + "consentTimeoutSeconds", AcceptOnStartup = Prefix + "acceptConnectionsOnStartup";
    private const string StatusBar = Prefix + "showStatusBarIndicator", LogLevel = Prefix + "logLevel", LogRetention = Prefix + "logRetentionDays";

    private static readonly string[] Monikers = [Read, Control, Mutate, ConsentTimeout, AcceptOnStartup, StatusBar, LogLevel, LogRetention];

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _raw = new();
    private bool _mutateAllowConfirmed;
    private bool _confirmPending;
    private IUnifiedSettingsService? _proxy;
    private IDisposable? _subscription;
    private bool _subscriptionLost;
    private bool _disposed;
    private volatile AppliedSettings _applied = AppliedSettings.Defaults;

    /// <summary>What the bridge enforces: replaced as one immutable snapshot, so a reader never sees a half-applied change.</summary>
    internal AppliedSettings Applied => _applied;

    /// <summary>Raised on a background thread after a permission changed; the argument summarises the change.</summary>
    public event EventHandler<string>? PermissionsChanged;

    /// <summary>One line per setting as Visual Studio returned it, for the extension log.</summary>
    public string ReadSummary { get; private set; } = "not read";

    /// <summary>Raised on a background thread after any value changed.</summary>
    public event EventHandler? AnyApplied;

    /// <summary>Reads the current values and subscribes to changes.</summary>
    public static async System.Threading.Tasks.Task<McpSettings> CreateAsync(IAsyncServiceProvider provider, CancellationToken cancellationToken)
    {
        var container = await provider.GetServiceAsync(typeof(SVsBrokeredServiceContainer)) as IBrokeredServiceContainer;
        if (container is null) throw new InvalidOperationException("Visual Studio's service broker is not available.");
        var broker = container.GetFullAccessServiceBroker();
#pragma warning disable ISB001 // The proxy must outlive this call: the change subscription runs over it. Dispose() releases it.
        var service = await broker.GetProxyAsync<IUnifiedSettingsService>(VisualStudioServices.VS2022_10.UnifiedSettingsService, cancellationToken);
#pragma warning restore ISB001
        if (service is null) throw new InvalidOperationException("Visual Studio's settings service is not available.");

        var settings = new McpSettings { _proxy = service };
        var results = await service.GetValuesAsync(Monikers, ReadOptions.NoRequirements, cancellationToken);
        settings.Apply(results, announce: false);
        settings._subscription = await service.SubscribeAsync(settings, Monikers, ReadOptions.NoRequirements, cancellationToken);
        return settings;
    }

    public void OnNext(SettingsUpdate value) => Apply(value.Values, announce: true);

    public void OnError(Exception error) => SubscriptionLost(error?.Message ?? "error");

    public void OnCompleted() => SubscriptionLost("completed");

    public void Dispose()
    {
        _disposed = true;
        _subscription?.Dispose();
        (_proxy as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Without the change feed the settings go stale, so a stored Mutate = Allow stops counting (Ask is enforced) and the
    /// subscription is retried in the background; a change of the settings is not followed until it is back.
    /// </summary>
    private void SubscriptionLost(string reason)
    {
        if (_disposed) return;
        lock (_gate)
        {
            _subscriptionLost = true;
            _mutateAllowConfirmed = false;
        }

        Recompute(announce: false);
        PermissionsChanged?.Invoke(this, $"Settings connection lost ({reason}): changes to the settings are not followed and Mutate = Allow is not enforced until it is restored");
        _ = ResubscribeAsync();
    }

    private async System.Threading.Tasks.Task ResubscribeAsync()
    {
        for (var attempt = 0; attempt < 5 && !_disposed; attempt++)
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
#pragma warning disable CA1031 // A failed retry only means the next attempt; the restrictive state stays in force.
            try
            {
                var proxy = _proxy;
                if (proxy is null || _disposed) return;
                var results = await proxy.GetValuesAsync(Monikers, ReadOptions.NoRequirements, CancellationToken.None);
                _subscription = await proxy.SubscribeAsync(this, Monikers, ReadOptions.NoRequirements, CancellationToken.None);
                lock (_gate) _subscriptionLost = false;
                Apply(results, announce: true);
                return;
            }
            catch (Exception)
            {
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    /// Raised on a background thread when the settings say Mutate = Allow and the user has not confirmed it yet. Until
    /// <see cref="ConfirmMutateAllow"/> is called the bridge enforces Ask, so a value written to the settings file (by
    /// anything) never silently lets the agent change program state.
    /// </summary>
    public event EventHandler? MutateAllowRequested;

    /// <summary>The user confirmed Mutate = Allow in the dialog: enforce it for as long as the setting stays Allow.</summary>
    public void ConfirmMutateAllow()
    {
        lock (_gate)
        {
            _mutateAllowConfirmed = true;
            _confirmPending = false;
        }

        Recompute(announce: true);
    }

    /// <summary>The user declined: keep enforcing Ask until the setting is changed again.</summary>
    public void DeclineMutateAllow()
    {
        lock (_gate) _confirmPending = false;
    }

    /// <summary>Asks for confirmation when the stored settings already say Mutate = Allow (call once the handler is attached).</summary>
    public void CheckMutateConfirmation() => RaiseMutateAllowRequestIfNeeded();

    private void Apply(IReadOnlyCollection<ReadResult> results, bool announce)
    {
        lock (_gate)
        {
            foreach (var result in results.Where(r => r.IsSuccess && r.Value is not null)) _raw[result.Moniker] = Unquote(result.Value!.Data);
            ReadSummary = string.Join("; ", results.Select(r => $"{r.Moniker.Substring(Prefix.Length)}={r.ResultCode}:{r.Value?.Data}"));
        }

        Recompute(announce);
    }

    private void Recompute(bool announce)
    {
        string? summary = null;
        lock (_gate)
        {
            var before = _applied.Permissions;
            var next = Snapshot();
            var changes = new List<string>();
            if (next.Permissions.Read != before.Read) changes.Add($"Read: {before.Read} → {next.Permissions.Read}");
            if (next.Permissions.Control != before.Control) changes.Add($"Control: {before.Control} → {next.Permissions.Control}");
            if (next.Permissions.Mutate != before.Mutate) changes.Add($"Mutate: {before.Mutate} → {next.Permissions.Mutate}");
            _applied = next;
            if (announce && changes.Count > 0) summary = "Settings changed: " + string.Join(", ", changes);
        }

        if (summary is not null) PermissionsChanged?.Invoke(this, summary);
        if (announce) AnyApplied?.Invoke(this, EventArgs.Empty);
        if (announce) RaiseMutateAllowRequestIfNeeded();
    }

    private void RaiseMutateAllowRequestIfNeeded()
    {
        // The change feed delivers the current values as soon as it is subscribed, before the package attached its handler:
        // an unhandled request must not count as pending, or the real one at startup would be swallowed.
        var handler = MutateAllowRequested;
        if (handler is null) return;
        lock (_gate)
        {
            if (Level(Mutate, PermissionLevel.Ask) != PermissionLevel.Allow || _mutateAllowConfirmed || _confirmPending || _subscriptionLost) return;
            _confirmPending = true;
        }

        handler(this, EventArgs.Empty);
    }

    /// <summary>Mutate as enforced: a stored Allow counts only after the user confirmed it; otherwise the previous level, or Ask.</summary>
    private PermissionLevel EffectiveMutate()
    {
        var stored = Level(Mutate, PermissionLevel.Ask);
        if (stored != PermissionLevel.Allow)
        {
            _mutateAllowConfirmed = false;
            return stored;
        }

        if (_mutateAllowConfirmed) return PermissionLevel.Allow;
        var previous = _applied.Permissions.Mutate;
        return previous == PermissionLevel.Allow ? PermissionLevel.Ask : previous;
    }

    private AppliedSettings Snapshot() => new(
        new PermissionSet { Read = Level(Read, PermissionLevel.Allow), Control = Level(Control, PermissionLevel.Allow), Mutate = EffectiveMutate() },
        TimeSpan.FromSeconds(Int(ConsentTimeout, 60, 15, 300)),
        Bool(AcceptOnStartup, true),
        Bool(StatusBar, true),
        Enum(LogLevel, LogVerbosity.Information),
        Int(LogRetention, 7, 1, 90));

    /// <summary>String settings come back JSON-encoded ("Deny" with its quotes).</summary>
    private static string Unquote(string? data) =>
        data is { Length: >= 2 } && data[0] == '"' && data[data.Length - 1] == '"' ? data.Substring(1, data.Length - 2) : data ?? "";

    private PermissionLevel Level(string moniker, PermissionLevel fallback) => Enum(moniker, fallback);

    private T Enum<T>(string moniker, T fallback) where T : struct =>
        _raw.TryGetValue(moniker, out var text) && System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(typeof(T), value) ? value : fallback;

    private bool Bool(string moniker, bool fallback) =>
        _raw.TryGetValue(moniker, out var text) && bool.TryParse(text, out var value) ? value : fallback;

    private int Int(string moniker, int fallback, int min, int max) =>
        _raw.TryGetValue(moniker, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(min, Math.Min(max, value))
            : fallback;
}
