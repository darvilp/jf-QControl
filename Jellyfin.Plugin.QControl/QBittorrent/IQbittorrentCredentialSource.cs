using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Resolves the current opaque qBittorrent credential.
/// </summary>
public interface IQbittorrentCredentialSource
{
    /// <summary>
    /// Resolves the current key for one bounded request.
    /// </summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The opaque validated API key, or <see langword="null"/> for an explicitly unauthenticated request.</returns>
    ValueTask<QbittorrentApiKey?> GetApiKeyAsync(CancellationToken cancellationToken);
}
