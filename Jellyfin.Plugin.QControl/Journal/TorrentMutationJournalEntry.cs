namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Per-hash durable mutation progress without torrent display data.
/// </summary>
/// <param name="Hash">The explicit torrent hash.</param>
/// <param name="MarkerAddStage">Marker-add progress.</param>
/// <param name="StopStage">Stop progress.</param>
/// <param name="StartStage">Start progress.</param>
/// <param name="MarkerRemoveStage">Marker-remove progress.</param>
public sealed record TorrentMutationJournalEntry(
    string Hash,
    JournalMutationStage MarkerAddStage,
    JournalMutationStage StopStage,
    JournalMutationStage StartStage,
    JournalMutationStage MarkerRemoveStage);
