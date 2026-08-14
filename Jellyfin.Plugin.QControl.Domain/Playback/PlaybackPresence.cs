using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.QControl.Domain.Playback;

/// <summary>
/// Derives playback presence from neutral session snapshots.
/// </summary>
public static class PlaybackPresence
{
    /// <summary>
    /// Evaluates the complete session set.
    /// </summary>
    /// <param name="sessions">The current neutral session snapshots.</param>
    /// <returns>The derived presence and participating session identifiers.</returns>
    public static PlaybackPresenceSnapshot Evaluate(
        IEnumerable<PlaybackSessionSnapshot> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var sessionIds = sessions
            .Where(session => session.HasCurrentMedia)
            .Select(session => session.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new PlaybackPresenceSnapshot(sessionIds.Length > 0, sessionIds);
    }
}
