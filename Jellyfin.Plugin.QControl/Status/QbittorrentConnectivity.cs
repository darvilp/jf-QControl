namespace Jellyfin.Plugin.QControl.Status;

/// <summary>
/// Bounded qBittorrent connectivity state.
/// </summary>
public enum QbittorrentConnectivity
{
    /// <summary>No validated connection is configured.</summary>
    Unconfigured,

    /// <summary>The read-only status probe succeeded.</summary>
    Connected,

    /// <summary>The read-only status probe failed.</summary>
    Failed,
}
