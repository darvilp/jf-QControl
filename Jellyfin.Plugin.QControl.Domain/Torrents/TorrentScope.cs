namespace Jellyfin.Plugin.QControl.Domain.Torrents;

/// <summary>
/// The configured torrent category scope.
/// </summary>
public enum TorrentScope
{
    /// <summary>
    /// All categorized and uncategorized torrents.
    /// </summary>
    All,

    /// <summary>
    /// Only torrents whose category exactly matches a selected name.
    /// </summary>
    SelectedCategories,
}
