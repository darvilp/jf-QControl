namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Secret-safe bounded failure categories persisted for administrator status.
/// </summary>
public enum JournalFailureCode
{
    /// <summary>
    /// Credential resolution failed.
    /// </summary>
    Credential,

    /// <summary>
    /// A bounded request timed out.
    /// </summary>
    Timeout,

    /// <summary>
    /// The qBittorrent endpoint was unreachable.
    /// </summary>
    Connection,

    /// <summary>
    /// qBittorrent rejected authentication.
    /// </summary>
    Authentication,

    /// <summary>
    /// A response was invalid.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// Server compatibility was unsupported.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// Journal persistence failed.
    /// </summary>
    JournalPersistence,
}
