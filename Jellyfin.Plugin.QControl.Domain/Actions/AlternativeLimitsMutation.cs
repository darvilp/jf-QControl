namespace Jellyfin.Plugin.QControl.Domain.Actions;

/// <summary>
/// A requested global alternative-limits mutation.
/// </summary>
public enum AlternativeLimitsMutation
{
    /// <summary>
    /// No mutation is required.
    /// </summary>
    None,

    /// <summary>
    /// Enable qBittorrent alternative limits.
    /// </summary>
    Enable,

    /// <summary>
    /// Disable qBittorrent alternative limits.
    /// </summary>
    Disable,
}
