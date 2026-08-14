using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.QControl.Domain.Activation;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Privacy-safe in-process lifecycle state for scheduling and later status APIs.
/// </summary>
public sealed record ProtectionCoordinatorSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectionCoordinatorSnapshot"/> class.
    /// </summary>
    /// <param name="phase">The current lifecycle phase.</param>
    /// <param name="sessionIds">Historical participating session identifiers.</param>
    /// <param name="releaseDueAt">The release deadline, if pending.</param>
    /// <param name="recoveryRequired">Whether automatic release is conservatively blocked.</param>
    public ProtectionCoordinatorSnapshot(
        ProtectionPhase phase,
        IEnumerable<string> sessionIds,
        DateTimeOffset? releaseDueAt,
        bool recoveryRequired)
    {
        Phase = phase;
        SessionIds = new ReadOnlyCollection<string>(sessionIds.ToArray());
        ReleaseDueAt = releaseDueAt;
        RecoveryRequired = recoveryRequired;
    }

    /// <summary>
    /// Gets the current lifecycle phase.
    /// </summary>
    public ProtectionPhase Phase { get; }

    /// <summary>
    /// Gets historical participating session identifiers without user or media data.
    /// </summary>
    public IReadOnlyList<string> SessionIds { get; }

    /// <summary>
    /// Gets the release deadline, if pending.
    /// </summary>
    public DateTimeOffset? ReleaseDueAt { get; }

    /// <summary>
    /// Gets a value indicating whether automatic release is blocked for recovery.
    /// </summary>
    public bool RecoveryRequired { get; }
}
