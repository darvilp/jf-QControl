using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.QControl.Domain.Playback;

namespace Jellyfin.Plugin.QControl.Domain.Activation;

/// <summary>
/// Reduces playback presence and explicit time into an activation lifecycle.
/// </summary>
public static class ProtectionActivationReducer
{
    /// <summary>
    /// Reduces one authoritative playback snapshot.
    /// </summary>
    /// <param name="current">The current activation state.</param>
    /// <param name="presence">The current playback presence.</param>
    /// <param name="now">The caller-provided current instant.</param>
    /// <param name="nextActivation">Values to use only if this reduction begins a new activation.</param>
    /// <returns>The next immutable activation state.</returns>
    public static ProtectionActivationState Reduce(
        ProtectionActivationState current,
        PlaybackPresenceSnapshot presence,
        DateTimeOffset now,
        ActivationRequest nextActivation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(nextActivation);

        return current.Phase switch
        {
            ProtectionPhase.Inactive => ReduceInactive(presence, now, nextActivation),
            ProtectionPhase.Protecting => ReduceProtecting(current, presence, now),
            ProtectionPhase.ReleasePending => ReduceReleasePending(current, presence, now),
            ProtectionPhase.Restoring => current,
            _ => throw new ArgumentOutOfRangeException(nameof(current), current.Phase, "Unknown phase."),
        };
    }

    /// <summary>
    /// Settles a completed restoration.
    /// </summary>
    /// <param name="current">The restoring activation.</param>
    /// <returns>The singleton inactive state.</returns>
    public static ProtectionActivationState CompleteRestoration(
        ProtectionActivationState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Phase != ProtectionPhase.Restoring)
        {
            throw new InvalidOperationException("Only a restoring activation can be completed.");
        }

        return ProtectionActivationState.Inactive;
    }

    private static ProtectionActivationState ReduceInactive(
        PlaybackPresenceSnapshot presence,
        DateTimeOffset now,
        ActivationRequest nextActivation)
    {
        if (!presence.IsPresent)
        {
            return ProtectionActivationState.Inactive;
        }

        if (nextActivation.ActivationId == Guid.Empty)
        {
            throw new ArgumentException("A new activation requires a non-empty identifier.", nameof(nextActivation));
        }

        if (nextActivation.ReleaseGrace < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextActivation),
                nextActivation.ReleaseGrace,
                "Release grace cannot be negative.");
        }

        return ProtectionActivationState.Active(
            ProtectionPhase.Protecting,
            nextActivation.ActivationId,
            now,
            nextActivation.ConfigurationRevision,
            nextActivation.ReleaseGrace,
            null,
            SortedUnique(presence.SessionIds));
    }

    private static ProtectionActivationState ReduceProtecting(
        ProtectionActivationState current,
        PlaybackPresenceSnapshot presence,
        DateTimeOffset now)
    {
        if (presence.IsPresent)
        {
            return Copy(
                current,
                ProtectionPhase.Protecting,
                null,
                MergeSessionIds(current.SessionIds, presence.SessionIds));
        }

        return Copy(
            current,
            ProtectionPhase.ReleasePending,
            now + current.ReleaseGrace!.Value,
            current.SessionIds);
    }

    private static ProtectionActivationState ReduceReleasePending(
        ProtectionActivationState current,
        PlaybackPresenceSnapshot presence,
        DateTimeOffset now)
    {
        if (presence.IsPresent)
        {
            return Copy(
                current,
                ProtectionPhase.Protecting,
                null,
                MergeSessionIds(current.SessionIds, presence.SessionIds));
        }

        if (now < current.ReleaseDueAt!.Value)
        {
            return current;
        }

        return Copy(
            current,
            ProtectionPhase.Restoring,
            null,
            current.SessionIds);
    }

    private static ProtectionActivationState Copy(
        ProtectionActivationState current,
        ProtectionPhase phase,
        DateTimeOffset? releaseDueAt,
        IReadOnlyList<string> sessionIds)
    {
        return ProtectionActivationState.Active(
            phase,
            current.ActivationId!.Value,
            current.StartedAt!.Value,
            current.ConfigurationRevision!.Value,
            current.ReleaseGrace!.Value,
            releaseDueAt,
            sessionIds);
    }

    private static string[] MergeSessionIds(
        IEnumerable<string> existing,
        IEnumerable<string> current)
    {
        return SortedUnique(existing.Concat(current));
    }

    private static string[] SortedUnique(IEnumerable<string> sessionIds)
    {
        return sessionIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
