using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.QControl.Domain.Activation;

/// <summary>
/// Immutable state for one protection activation lifecycle.
/// </summary>
public sealed record ProtectionActivationState
{
    private ProtectionActivationState(
        ProtectionPhase phase,
        Guid? activationId,
        DateTimeOffset? startedAt,
        long? configurationRevision,
        TimeSpan? releaseGrace,
        DateTimeOffset? releaseDueAt,
        IReadOnlyList<string> sessionIds)
    {
        Phase = phase;
        ActivationId = activationId;
        StartedAt = startedAt;
        ConfigurationRevision = configurationRevision;
        ReleaseGrace = releaseGrace;
        ReleaseDueAt = releaseDueAt;
        SessionIds = Array.AsReadOnly(sessionIds.ToArray());
    }

    /// <summary>
    /// Gets the singleton inactive state.
    /// </summary>
    public static ProtectionActivationState Inactive { get; } = new(
        ProtectionPhase.Inactive,
        null,
        null,
        null,
        null,
        null,
        []);

    /// <summary>
    /// Gets the lifecycle phase.
    /// </summary>
    public ProtectionPhase Phase { get; }

    /// <summary>
    /// Gets the current activation identifier, if active.
    /// </summary>
    public Guid? ActivationId { get; }

    /// <summary>
    /// Gets the activation start instant, if active.
    /// </summary>
    public DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// Gets the behavior configuration revision fixed for this activation.
    /// </summary>
    public long? ConfigurationRevision { get; }

    /// <summary>
    /// Gets the release grace fixed for this activation.
    /// </summary>
    public TimeSpan? ReleaseGrace { get; }

    /// <summary>
    /// Gets the release deadline while release is pending.
    /// </summary>
    public DateTimeOffset? ReleaseDueAt { get; }

    /// <summary>
    /// Gets the deterministic identifiers of sessions that participated in the activation.
    /// </summary>
    public IReadOnlyList<string> SessionIds { get; }

    /// <summary>
    /// Creates one internally valid active state.
    /// </summary>
    /// <param name="phase">The active lifecycle phase.</param>
    /// <param name="activationId">The activation identifier.</param>
    /// <param name="startedAt">The activation start instant.</param>
    /// <param name="configurationRevision">The snapshotted configuration revision.</param>
    /// <param name="releaseGrace">The snapshotted release grace.</param>
    /// <param name="releaseDueAt">The optional pending-release deadline.</param>
    /// <param name="sessionIds">The participating session identifiers.</param>
    /// <returns>A valid active state.</returns>
    internal static ProtectionActivationState Active(
        ProtectionPhase phase,
        Guid activationId,
        DateTimeOffset startedAt,
        long configurationRevision,
        TimeSpan releaseGrace,
        DateTimeOffset? releaseDueAt,
        IReadOnlyList<string> sessionIds)
    {
        return new ProtectionActivationState(
            phase,
            activationId,
            startedAt,
            configurationRevision,
            releaseGrace,
            releaseDueAt,
            sessionIds);
    }
}
