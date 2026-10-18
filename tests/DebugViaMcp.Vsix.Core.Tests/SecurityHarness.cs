using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix.Core.Tests;

/// <summary>A handler wired to fakes for the prompt, timers and audit folder.</summary>
public sealed class SecurityHarness : IDisposable
{
    public SecurityHarness(FakeDte? dte = null, IUiThread? ui = null)
    {
        Dte = dte ?? new FakeDte();
        AuditDir = Directory.CreateTempSubdirectory("debugviamcp-gate-").FullName;
        Audit = new AuditLog(AuditDir, () => 7, new ListActivity(Activity), new NullLog());
        Consent = new ConsentGate(Prompt, () => Settings.ConsentTimeout, Delay.Delay);
        Security = new BridgeSecurity(Settings, Consent, Audit, Access);
        Service = new DebugBridgeService(Dte, ui ?? new InlineUiThread(), Seq, new NullLog(), "0.1.0");
        Handler = new BridgeConnectionHandler(
            Service, "secret", Security, new NullLog(),
            new ConnectionHooks(_ => { }, () =>
            {
                RevokedNotified = true;
                Interlocked.Increment(ref _drops);
                return Task.CompletedTask;
            }, connectionId: 1));
    }

    /// <summary>A folder inside the solution folder of <see cref="FakeDte"/>: connections from it are approved without a question.</summary>
    public const string TrustedFolder = @"C:\src\Demo";

    private int _drops;
    private int _lastConnectionId = 1;
    private readonly List<BridgeConnectionHandler> _extra = [];

    public FakeDte Dte { get; }
    public DebugBridgeService Service { get; }
    public EventSeq Seq { get; } = new();
    public AiAccessSwitch Access { get; } = new(enabled: true);
    public int Drops => Volatile.Read(ref _drops);
    public bool RevokedNotified { get; private set; }
    public TestSettings Settings { get; } = new();
    public FakePrompt Prompt { get; } = new();
    public ManualDelay Delay { get; } = new();
    public List<string> Activity { get; } = [];
    public string AuditDir { get; }
    public AuditLog Audit { get; }
    public ConsentGate Consent { get; }
    public BridgeSecurity Security { get; }
    public BridgeConnectionHandler Handler { get; }

    public static CallContext Ctx(string id = "c-000001") => new() { CallId = id };

    public Task<HandshakeResponse> HandshakeAsync(string client = "claude-code", string? folder = TrustedFolder) =>
        Handler.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major, ClientName = client, ClientWorkingDirectory = folder }, CancellationToken.None);

    /// <summary>Another connection to the same Visual Studio (shared security, service and DTE), already through its handshake.</summary>
    public async Task<BridgeConnectionHandler> AddConnectionAsync(string client = "claude-code", string? folder = TrustedFolder)
    {
        var id = Interlocked.Increment(ref _lastConnectionId);
        var handler = new BridgeConnectionHandler(Service, "secret", Security, new NullLog(), new ConnectionHooks(_ => { }, () => Task.CompletedTask, id));
        _extra.Add(handler);
        await handler.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major, ClientName = client, ClientWorkingDirectory = folder }, CancellationToken.None);
        return handler;
    }

    public string[] AuditLines() =>
        Directory.GetFiles(AuditDir, "audit-*.log").SelectMany(File.ReadAllLines).ToArray();

    public void Dispose()
    {
        Handler.Dispose();
        foreach (var extra in _extra) extra.Dispose();
        try { Directory.Delete(AuditDir, recursive: true); }
        catch (IOException) { }
    }

    private sealed class ListActivity(List<string> lines) : IActivitySink
    {
        public void Write(string line)
        {
            lock (lines) lines.Add(line);
        }
    }
}
