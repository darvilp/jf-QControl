namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Classification of one journal load attempt.
/// </summary>
public enum ActivationJournalLoadStatus
{
    /// <summary>
    /// No journal exists.
    /// </summary>
    Missing,

    /// <summary>
    /// A valid journal belongs to the current uninterrupted process.
    /// </summary>
    Active,

    /// <summary>
    /// A valid journal belongs to a prior process and requires recovery.
    /// </summary>
    Interrupted,

    /// <summary>
    /// The journal is malformed or violates invariants.
    /// </summary>
    Corrupt,

    /// <summary>
    /// The journal schema is not supported by this plugin version.
    /// </summary>
    UnsupportedSchema,
}
