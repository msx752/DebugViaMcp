using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Core.Tests;

/// <summary>A handler wired to fakes for the prompt, timers and audit folder.</summary>
public sealed class SecurityHarness : IDisposable
{
    public SecurityHarness(FakeDte? dte = null)
    {
        Dte = dte ?? new FakeDte();
        AuditDir = Directory.CreateTempSubdirectory("vsmcpdebugger-gate-").FullName;
        Audit = new AuditLog(AuditDir, () => 7, new ListActivity(Activity), new NullLog());
        Consent = new ConsentGate(Prompt, () => Settings.ConsentTimeout, Delay.Delay);
        Security = new BridgeSecurity(Settings, Consent, Audit, Access);
        Handler = new BridgeConnectionHandler(
            new DebugBridgeService(Dte, new InlineUiThread(), Seq, new NullLog(), "0.1.0"), "secret", Security, new NullLog(),
            new ConnectionHooks(() => { }, () =>
            {
                RevokedNotified = true;
                Interlocked.Increment(ref _drops);
                return Task.CompletedTask;
            }));
    }

    private int _drops;

    public FakeDte Dte { get; }
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

    public Task<HandshakeResponse> HandshakeAsync(string client = "claude-code") =>
        Handler.HandshakeAsync(new HandshakeRequest { Token = "secret", ProtocolMajor = ProtocolInfo.Major, ClientName = client }, CancellationToken.None);

    public string[] AuditLines() =>
        Directory.GetFiles(AuditDir, "audit-*.log").SelectMany(File.ReadAllLines).ToArray();

    public void Dispose()
    {
        Handler.Dispose();
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
