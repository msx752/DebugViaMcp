using System;
using DebugViaMcp.Vsix.Core.Security;

namespace DebugViaMcp.Vsix.Core.Instances;

/// <summary>What an operation needs from the Visual Studio lease.</summary>
public enum LeaseNeed
{
    /// <summary>Works whoever holds the lease (connect, release).</summary>
    None,

    /// <summary>Allowed when the lease is free or the caller's; never takes it.</summary>
    Read,

    /// <summary>Changes Visual Studio state: takes the lease, and is refused while another agent holds it.</summary>
    Change,
}

public static class LeaseRules
{
    public static LeaseNeed For(Operation operation) => operation switch
    {
        Operation.Connect or Operation.Release => LeaseNeed.None,

        // Read tier, but it moves Visual Studio's current frame.
        Operation.FrameSelect => LeaseNeed.Change,

        _ => PermissionPolicy.TierOf(operation) == PermissionTier.Read ? LeaseNeed.Read : LeaseNeed.Change,
    };
}
