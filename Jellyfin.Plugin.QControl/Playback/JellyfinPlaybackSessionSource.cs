using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Playback;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.QControl.Playback;

/// <summary>
/// Projects Jellyfin's authoritative session collection into neutral playback state.
/// </summary>
public sealed class JellyfinPlaybackSessionSource : IPlaybackSessionSource
{
    private readonly ISessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPlaybackSessionSource"/> class.
    /// </summary>
    /// <param name="sessionManager">Jellyfin's current-session authority.</param>
    public JellyfinPlaybackSessionSource(ISessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        _sessionManager = sessionManager;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaybackSessionSnapshot>> ReadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PlaybackSessionSnapshot> snapshots = _sessionManager.Sessions
            .Select(session => new PlaybackSessionSnapshot(
                session.Id,
                session.NowPlayingItem is not null,
                session.PlayState?.IsPaused ?? false))
            .OrderBy(session => session.SessionId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(snapshots);
    }
}
