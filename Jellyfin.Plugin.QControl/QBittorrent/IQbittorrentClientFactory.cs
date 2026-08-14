using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Builds a narrow client for either an activation-fixed endpoint or a connection candidate.
/// </summary>
public interface IQbittorrentClientFactory
{
    /// <summary>Creates a client pinned to the activation endpoint with the latest credential.</summary>
    /// <param name="endpoint">The credential-free activation endpoint.</param>
    /// <returns>The allowlisted client.</returns>
    IQbittorrentClient Create(QbittorrentEndpointIdentity endpoint);

    /// <summary>Creates a client for a complete in-memory configuration candidate.</summary>
    /// <param name="configuration">The complete candidate including its resolved credential.</param>
    /// <returns>The allowlisted client.</returns>
    IQbittorrentClient Create(PluginConfiguration configuration);
}
