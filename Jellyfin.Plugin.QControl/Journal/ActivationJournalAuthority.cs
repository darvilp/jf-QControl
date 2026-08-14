namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Automatic authority carried by one loaded journal.
/// </summary>
public enum ActivationJournalAuthority
{
    /// <summary>
    /// No automatic external mutation is authorized.
    /// </summary>
    None,

    /// <summary>
    /// Protection may be reasserted, but automatic release is forbidden.
    /// </summary>
    ProtectOnly,

    /// <summary>
    /// The uninterrupted owner may protect and normally release.
    /// </summary>
    Full,
}
