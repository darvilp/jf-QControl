namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Stable, secret-safe qBittorrent client failure categories.
/// </summary>
public enum QbittorrentClientError
{
    /// <summary>
    /// A configured request deadline elapsed.
    /// </summary>
    Timeout,

    /// <summary>
    /// The remote endpoint could not be reached securely.
    /// </summary>
    Connection,

    /// <summary>
    /// qBittorrent rejected authentication.
    /// </summary>
    Authentication,

    /// <summary>
    /// qBittorrent returned an unexpected response.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// The qBittorrent or Web API version is unsupported.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// A configured credential could not be resolved.
    /// </summary>
    Credential,
}
