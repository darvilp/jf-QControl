using System;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Captures a validated behavior snapshot for a new playback activation.
/// </summary>
public interface IActivationJournalFactory
{
    /// <summary>
    /// Creates a complete new journal, or returns null while configuration is inert.
    /// </summary>
    /// <param name="presence">The authoritative playback presence.</param>
    /// <param name="processInstanceId">The uninterrupted process identity.</param>
    /// <param name="now">The activation start instant.</param>
    /// <returns>A complete journal or null when no action is enabled.</returns>
    ActivationJournalDocument? Create(
        PlaybackPresenceSnapshot presence,
        Guid processInstanceId,
        DateTimeOffset now);
}
