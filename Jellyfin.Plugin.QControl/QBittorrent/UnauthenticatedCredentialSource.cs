using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Explicitly requests qBittorrent without an authorization header.
/// </summary>
public sealed class UnauthenticatedCredentialSource : IQbittorrentCredentialSource
{
    /// <inheritdoc />
    public ValueTask<QbittorrentApiKey?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<QbittorrentApiKey?>(null);
    }
}
