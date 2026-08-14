namespace Jellyfin.Plugin.QControl.Domain.Playback;

/// <summary>
/// Framework-neutral playback state for one Jellyfin session.
/// </summary>
/// <param name="SessionId">The stable session identifier.</param>
/// <param name="HasCurrentMedia">Whether the player has a current media item.</param>
/// <param name="IsPaused">Whether current media is paused.</param>
public sealed record PlaybackSessionSnapshot(
    string SessionId,
    bool HasCurrentMedia,
    bool IsPaused);
