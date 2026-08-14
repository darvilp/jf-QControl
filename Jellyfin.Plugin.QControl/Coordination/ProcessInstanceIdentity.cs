using System;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Identifies one uninterrupted Jellyfin plugin process for journal authority.
/// </summary>
/// <param name="Value">The non-empty process instance identifier.</param>
public sealed record ProcessInstanceIdentity(Guid Value)
{
    /// <summary>Creates a new random process instance identity.</summary>
    /// <returns>A non-empty identity.</returns>
    public static ProcessInstanceIdentity Create() => new(Guid.NewGuid());
}
