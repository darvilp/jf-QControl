namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Selects where QControl reads the qBittorrent API key.
/// </summary>
public enum QbittorrentCredentialMode
{
    /// <summary>
    /// Read the key from Jellyfin plugin configuration storage.
    /// </summary>
    StoredApiKey,

    /// <summary>
    /// Read the key from a platform-native file on every request.
    /// </summary>
    SecretFile,
}
