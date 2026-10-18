namespace DebugViaMcp.Vsix.Core.Debugging;

/// <summary>
/// The breakpoint debug_run_to is waiting for. Owned = we created it and must delete it; a user breakpoint already at
/// that location is used but never deleted. Only touched on the UI thread.
/// </summary>
internal sealed class RunToState
{
    private Armed? _armed;

    public string? TargetId => _armed?.Id;

    public void Arm(string id, bool owned, object owner) => _armed = new Armed(id, owned, owner);

    public bool Owns(string id) => _armed is { Owned: true } a && a.Id == id;

    /// <summary>Disarms and returns the target; with <paramref name="owner"/>, only if that connection armed it.</summary>
    public (string Id, bool Owned)? Take(object? owner = null)
    {
        if (_armed is not { } a || (owner is not null && !ReferenceEquals(owner, a.Owner))) return null;
        _armed = null;
        return (a.Id, a.Owned);
    }

    private sealed class Armed(string id, bool owned, object owner)
    {
        public string Id { get; } = id;
        public bool Owned { get; } = owned;
        public object Owner { get; } = owner;
    }
}
