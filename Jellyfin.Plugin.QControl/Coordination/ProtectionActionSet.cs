using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Composes independent actions while preserving the latest durable state after failures.
/// </summary>
public sealed class ProtectionActionSet : IProtectionActionSet
{
    private readonly IAlternativeLimitsActionService _alternativeLimits;
    private readonly IStopTorrentsActionService _stopTorrents;
    private readonly IActivationJournalStore _journalStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectionActionSet"/> class.
    /// </summary>
    /// <param name="alternativeLimits">The independent Alternative Limits action.</param>
    /// <param name="stopTorrents">The independent Stop Torrents action.</param>
    /// <param name="journalStore">The shared durable activation state.</param>
    public ProtectionActionSet(
        IAlternativeLimitsActionService alternativeLimits,
        IStopTorrentsActionService stopTorrents,
        IActivationJournalStore journalStore)
    {
        ArgumentNullException.ThrowIfNull(alternativeLimits);
        ArgumentNullException.ThrowIfNull(stopTorrents);
        ArgumentNullException.ThrowIfNull(journalStore);
        _alternativeLimits = alternativeLimits;
        _stopTorrents = stopTorrents;
        _journalStore = journalStore;
    }

    /// <inheritdoc />
    public async Task<ProtectionActionSetResult> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        JournalFailureCode? failure = null;

        try
        {
            journal = await _alternativeLimits
                .ReconcileProtectionAsync(journal, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QbittorrentClientException exception)
        {
            failure = QbittorrentFailureMapper.Map(exception.Error);
            journal = await ReloadLatestAsync(journal, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            journal = await _stopTorrents
                .ReconcileProtectionAsync(journal, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QbittorrentClientException exception)
        {
            failure ??= QbittorrentFailureMapper.Map(exception.Error);
            journal = await ReloadLatestAsync(journal, cancellationToken).ConfigureAwait(false);
        }

        return new ProtectionActionSetResult(journal, false, failure);
    }

    /// <inheritdoc />
    public async Task<ProtectionActionSetResult> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        JournalFailureCode? failure = null;

        try
        {
            journal = await _alternativeLimits
                .ReconcileRestorationAsync(journal, authority, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QbittorrentClientException exception)
        {
            failure = QbittorrentFailureMapper.Map(exception.Error);
            journal = await ReloadLatestAsync(journal, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            journal = await _stopTorrents
                .ReconcileRestorationAsync(journal, authority, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QbittorrentClientException exception)
        {
            failure ??= QbittorrentFailureMapper.Map(exception.Error);
            journal = await ReloadLatestAsync(journal, cancellationToken).ConfigureAwait(false);
        }

        return new ProtectionActionSetResult(
            journal,
            failure is null && IsRestorationSettled(journal),
            failure);
    }

    private async Task<ActivationJournalDocument> ReloadLatestAsync(
        ActivationJournalDocument prior,
        CancellationToken cancellationToken)
    {
        var loaded = await _journalStore
            .LoadAsync(prior.ProcessInstanceId, cancellationToken)
            .ConfigureAwait(false);
        return loaded.Document ?? prior;
    }

    private static bool IsRestorationSettled(ActivationJournalDocument journal)
    {
        var alternativeLimitsSettled = !journal.Configuration.AlternativeLimitsEnabled
            || !journal.AlternativeLimits.EnabledByActivation
            || journal.AlternativeLimits.DisableStage == JournalMutationStage.Confirmed;
        var torrentsSettled = !journal.Configuration.StopTorrentsEnabled
            || journal.Torrents.All(entry =>
                entry.StartStage != JournalMutationStage.IntentPersisted
                && entry.MarkerRemoveStage != JournalMutationStage.IntentPersisted);
        return alternativeLimitsSettled && torrentsSettled;
    }
}
