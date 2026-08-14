using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.QControl.Domain.Playback;

/// <summary>
/// Immutable playback presence derived from a complete session snapshot.
/// </summary>
public sealed record PlaybackPresenceSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackPresenceSnapshot"/> class.
    /// </summary>
    /// <param name="isPresent">Whether at least one session has current media.</param>
    /// <param name="sessionIds">The deterministic identifiers of participating sessions.</param>
    public PlaybackPresenceSnapshot(bool isPresent, IEnumerable<string> sessionIds)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        var copiedSessionIds = sessionIds.ToArray();
        if (isPresent != (copiedSessionIds.Length > 0))
        {
            throw new ArgumentException(
                "Playback presence must agree with the participating session identifiers.",
                nameof(sessionIds));
        }

        IsPresent = isPresent;
        SessionIds = Array.AsReadOnly(copiedSessionIds);
    }

    /// <summary>
    /// Gets a value indicating whether at least one session has current media.
    /// </summary>
    public bool IsPresent { get; }

    /// <summary>
    /// Gets the deterministic identifiers of participating sessions.
    /// </summary>
    public IReadOnlyList<string> SessionIds { get; }
}
