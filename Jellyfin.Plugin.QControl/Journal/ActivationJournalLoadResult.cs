namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Neutral journal load state and its automatic authority.
/// </summary>
/// <param name="Status">The load classification.</param>
/// <param name="Authority">The resulting automatic authority.</param>
/// <param name="Document">The valid document, when one was accepted.</param>
public sealed record ActivationJournalLoadResult(
    ActivationJournalLoadStatus Status,
    ActivationJournalAuthority Authority,
    ActivationJournalDocument? Document);
