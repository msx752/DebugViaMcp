using System;
using System.Collections.Generic;
using System.Linq;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>Disconnect AI / Reconnect AI. One per Visual Studio session; Changed may be raised on any thread.</summary>
public sealed class AiAccessSwitch(bool enabled)
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Client, Action Drop)> _connections = [];
    private bool _enabled = enabled;
    private int _nextId;

    public event EventHandler? Changed;

    public bool Enabled
    {
        get { lock (_gate) return _enabled; }
    }

    public IReadOnlyList<string> Clients
    {
        get { lock (_gate) return _connections.Values.Select(c => c.Client).ToArray(); }
    }

    /// <returns>A registration to dispose when the connection closes, or null while AI access is disconnected.</returns>
    public IDisposable? TryAttach(string client, Action drop)
    {
        int id;
        lock (_gate)
        {
            if (!_enabled) return null;
            id = ++_nextId;
            _connections[id] = (client, drop);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new Registration(this, id);
    }

    public void Disconnect()
    {
        (string Client, Action Drop)[] doomed;
        lock (_gate)
        {
            if (!_enabled) return;
            _enabled = false;
            doomed = _connections.Values.ToArray();
            _connections.Clear();
        }

        foreach (var c in doomed) c.Drop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reconnect()
    {
        lock (_gate)
        {
            if (_enabled) return;
            _enabled = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Detach(int id)
    {
        bool removed;
        lock (_gate) removed = _connections.Remove(id);
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Registration(AiAccessSwitch owner, int id) : IDisposable
    {
        public void Dispose() => owner.Detach(id);
    }
}
