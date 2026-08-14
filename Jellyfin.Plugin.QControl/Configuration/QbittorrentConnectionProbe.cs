using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Reads compatibility and categories without invoking any qBittorrent mutation endpoint.
/// </summary>
public sealed class QbittorrentConnectionProbe : IQbittorrentConnectionProbe
{
    private readonly IQbittorrentClientFactory _clientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="QbittorrentConnectionProbe"/> class.
    /// </summary>
    /// <param name="clientFactory">The candidate client factory.</param>
    public QbittorrentConnectionProbe(IQbittorrentClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    /// <inheritdoc />
    public async Task<QbittorrentConnectionProbeResult> ProbeAsync(
        PluginConfiguration candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        try
        {
            var client = _clientFactory.Create(candidate);
            var server = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
            var categories = await client.GetCategoriesAsync(cancellationToken).ConfigureAwait(false);
            return QbittorrentConnectionProbeResult.Connected(
                server.ApplicationVersion,
                server.WebApiVersion,
                categories);
        }
        catch (QbittorrentClientException exception)
        {
            return QbittorrentConnectionProbeResult.Failed(
                QbittorrentFailureMapper.Map(exception.Error));
        }
    }
}
