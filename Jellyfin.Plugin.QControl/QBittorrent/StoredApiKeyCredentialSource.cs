using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Supplies a key held by the persisted plugin configuration boundary.
/// </summary>
public sealed class StoredApiKeyCredentialSource : IQbittorrentCredentialSource
{
    private readonly QbittorrentApiKey _apiKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredApiKeyCredentialSource"/> class.
    /// </summary>
    /// <param name="apiKey">The stored key content.</param>
    public StoredApiKeyCredentialSource(string apiKey)
    {
        _apiKey = QbittorrentApiKey.Create(apiKey);
    }

    /// <inheritdoc />
    public ValueTask<QbittorrentApiKey> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_apiKey);
    }
}
