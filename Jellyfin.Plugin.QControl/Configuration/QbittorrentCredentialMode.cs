namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Selects where QControl reads the qBittorrent API key.
/// </summary>
public enum QbittorrentCredentialMode
{
    /// <summary>
    /// Read the key from Jellyfin plugin configuration storage.
    /// </summary>
    StoredApiKey = 0,

    /// <summary>
    /// Read the key from a platform-native file on every request.
    /// </summary>
    SecretFile = 1,

    /// <summary>
    /// Send no authorization header and rely on qBittorrent's configured authentication bypass.
    /// </summary>
    Unauthenticated = 2,
}
