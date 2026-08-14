using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Playback;

namespace Jellyfin.Plugin.QControl.Playback;

/// <summary>
/// Reads a complete privacy-neutral playback-session snapshot.
/// </summary>
public interface IPlaybackSessionSource
{
    /// <summary>
    /// Reads all current sessions without media, user, or device display data.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The deterministic neutral session snapshot.</returns>
    Task<IReadOnlyList<PlaybackSessionSnapshot>> ReadAsync(
        CancellationToken cancellationToken);
}
