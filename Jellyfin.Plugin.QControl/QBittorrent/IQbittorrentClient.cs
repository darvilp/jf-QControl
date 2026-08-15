using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// The complete allowlisted qBittorrent boundary used by QControl.
/// </summary>
public interface IQbittorrentClient
{
    /// <summary>
    /// Reads and validates server compatibility.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The compatible server versions.</returns>
    Task<QbittorrentServerInfo> GetServerInfoAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists neutral torrent-policy snapshots.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Deterministic neutral torrent snapshots.</returns>
    Task<IReadOnlyList<TorrentSnapshot>> GetTorrentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists exact category names.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Deterministic exact category names.</returns>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists all exact registered tag names.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Deterministic exact registered tag names.</returns>
    Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads whether Alternative Limits mode is enabled.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Whether Alternative Limits mode is enabled.</returns>
    Task<bool> GetAlternativeLimitsEnabledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds one exact tag to explicit hashes.
    /// </summary>
    /// <param name="hashes">Explicit torrent hashes.</param>
    /// <param name="tag">The one exact tag.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing the accepted mutation.</returns>
    Task AddTagAsync(
        IEnumerable<string> hashes,
        string tag,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops explicit torrent hashes.
    /// </summary>
    /// <param name="hashes">Explicit torrent hashes.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing the accepted mutation.</returns>
    Task StopTorrentsAsync(
        IEnumerable<string> hashes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts explicit torrent hashes.
    /// </summary>
    /// <param name="hashes">Explicit torrent hashes.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing the accepted mutation.</returns>
    Task StartTorrentsAsync(
        IEnumerable<string> hashes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes one exact tag from explicit hashes.
    /// </summary>
    /// <param name="hashes">Explicit torrent hashes.</param>
    /// <param name="tag">The one exact tag.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing the accepted mutation.</returns>
    Task RemoveTagAsync(
        IEnumerable<string> hashes,
        string tag,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deterministically sets Alternative Limits mode.
    /// </summary>
    /// <param name="enabled">The desired enabled state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing the accepted mutation.</returns>
    Task SetAlternativeLimitsEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken);
}
