using System;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Keeps the hosted coordinator observant but inert until configuration is validated.
/// </summary>
public sealed class UnconfiguredActivationJournalFactory : IActivationJournalFactory
{
    /// <inheritdoc />
    public ActivationJournalDocument? Create(
        PlaybackPresenceSnapshot presence,
        Guid processInstanceId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(presence);
        return null;
    }
}
