namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Durable progress for one non-transactional external mutation.
/// </summary>
public enum JournalMutationStage
{
    /// <summary>
    /// No mutation is currently planned.
    /// </summary>
    None,

    /// <summary>
    /// Intent is durable but external confirmation is not.
    /// </summary>
    IntentPersisted,

    /// <summary>
    /// The external result was read back and confirmed.
    /// </summary>
    Confirmed,
}
