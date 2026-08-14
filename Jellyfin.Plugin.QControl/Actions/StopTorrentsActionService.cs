using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Actions;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Actions;

/// <summary>
/// Serializes journaled, read-back-confirmed Stop Torrents reconciliation.
/// </summary>
public sealed class StopTorrentsActionService : IStopTorrentsActionService, IDisposable
{
    private readonly IQbittorrentClient _qbit;
    private readonly IActivationJournalStore _journalStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="StopTorrentsActionService"/> class.
    /// </summary>
    /// <param name="qbit">The allowlisted qBittorrent boundary.</param>
    /// <param name="journalStore">The durable activation journal boundary.</param>
    public StopTorrentsActionService(
        IQbittorrentClient qbit,
        IActivationJournalStore journalStore)
    {
        ArgumentNullException.ThrowIfNull(qbit);
        ArgumentNullException.ThrowIfNull(journalStore);
        _qbit = qbit;
        _journalStore = journalStore;
    }

    /// <inheritdoc />
    public async Task<ActivationJournalDocument> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!journal.Configuration.StopTorrentsEnabled)
        {
            return journal;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReconcileProtectionCoreAsync(journal, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ActivationJournalDocument> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!journal.Configuration.StopTorrentsEnabled)
        {
            return journal;
        }

        if (authority != ActivationJournalAuthority.Full)
        {
            throw new InvalidOperationException(
                "Full journal authority is required for automatic restoration.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReconcileRestorationCoreAsync(journal, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<ActivationJournalDocument> ReconcileProtectionCoreAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        var policy = CreatePolicy(journal);
        var torrents = await _qbit.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
        var snapshots = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.Ordinal);

        var priorReadback = ConfirmPresentMarkers(journal, snapshots, policy.MarkerTag);
        priorReadback = ConfirmStoppedTorrents(priorReadback, snapshots);
        journal = await PersistIfChangedAsync(journal, priorReadback, cancellationToken)
            .ConfigureAwait(false);

        var acquisition = TorrentActionPlanner.PlanProtection(torrents, policy);

        var intentDocument = UpsertMarkerIntents(
            journal,
            acquisition.StopHashes,
            snapshots,
            policy.MarkerTag);
        journal = await PersistIfChangedAsync(journal, intentDocument, cancellationToken)
            .ConfigureAwait(false);

        if (acquisition.AddMarkerTagHashes.Count > 0)
        {
            await _qbit
                .AddTagAsync(acquisition.AddMarkerTagHashes, policy.MarkerTag, cancellationToken)
                .ConfigureAwait(false);
        }

        torrents = await _qbit.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
        snapshots = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.Ordinal);
        var confirmedMarkerDocument = ConfirmPresentMarkers(journal, snapshots, policy.MarkerTag);
        journal = await PersistIfChangedAsync(
                journal,
                confirmedMarkerDocument,
                cancellationToken)
            .ConfigureAwait(false);

        var currentPlan = TorrentActionPlanner.PlanProtection(torrents, policy);
        var stopHashes = currentPlan.StopHashes
            .Where(hash => HasConfirmedMarker(journal, hash))
            .Where(hash => snapshots.TryGetValue(hash, out var torrent)
                && torrent.Tags.Contains(policy.MarkerTag))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (stopHashes.Length == 0)
        {
            return journal;
        }

        var stopIntentDocument = SetStage(
            journal,
            stopHashes,
            entry => entry with { StopStage = JournalMutationStage.IntentPersisted });
        journal = await PersistIfChangedAsync(
                journal,
                stopIntentDocument,
                cancellationToken)
            .ConfigureAwait(false);

        await _qbit.StopTorrentsAsync(stopHashes, cancellationToken).ConfigureAwait(false);

        torrents = await _qbit.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
        snapshots = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.Ordinal);
        var stoppedHashes = stopHashes
            .Where(hash => snapshots.TryGetValue(hash, out var torrent) && torrent.IsStopped)
            .ToArray();
        var stopConfirmedDocument = SetStage(
            journal,
            stoppedHashes,
            entry => entry with { StopStage = JournalMutationStage.Confirmed });
        return await PersistIfChangedAsync(
                journal,
                stopConfirmedDocument,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ActivationJournalDocument> ReconcileRestorationCoreAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        var policy = CreatePolicy(journal);
        var torrents = await _qbit.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
        var snapshots = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.Ordinal);

        var priorRemovalConfirmations = ConfirmAbsentMarkers(journal, snapshots, policy.MarkerTag);
        journal = await PersistIfChangedAsync(
                journal,
                priorRemovalConfirmations,
                cancellationToken)
            .ConfigureAwait(false);

        var plan = TorrentActionPlanner.PlanRestoration(torrents, policy);
        var restorationIntent = UpsertRestorationIntents(journal, torrents, policy);
        journal = await PersistIfChangedAsync(journal, restorationIntent, cancellationToken)
            .ConfigureAwait(false);

        if (plan.StartHashes.Count > 0)
        {
            await _qbit.StartTorrentsAsync(plan.StartHashes, cancellationToken)
                .ConfigureAwait(false);
            torrents = await _qbit.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
            snapshots = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.Ordinal);
        }

        var markerRemovalIntents = ConfirmStartsAndPlanMarkerRemoval(
            journal,
            torrents,
            policy);
        journal = await PersistIfChangedAsync(
                journal,
                markerRemovalIntents,
                cancellationToken)
            .ConfigureAwait(false);

        var currentPlan = TorrentActionPlanner.PlanRestoration(torrents, policy);
        var removeMarkerHashes = currentPlan.RemoveMarkerTagHashes
            .Where(hash => HasMarkerRemovalIntent(journal, hash))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (removeMarkerHashes.Length == 0)
        {
            return journal;
        }

        await _qbit
            .RemoveTagAsync(removeMarkerHashes, policy.MarkerTag, cancellationToken)
            .ConfigureAwait(false);
        torrents = await _qbit.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
        snapshots = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.Ordinal);

        var removalConfirmations = ConfirmAbsentMarkers(journal, snapshots, policy.MarkerTag);
        return await PersistIfChangedAsync(
                journal,
                removalConfirmations,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static TorrentSelectionPolicy CreatePolicy(ActivationJournalDocument journal)
    {
        var configuration = journal.Configuration;
        return new TorrentSelectionPolicy(
            configuration.StopScope,
            configuration.SelectedCategories,
            configuration.IncludeIncomplete,
            configuration.IncludeCompleted,
            configuration.MarkerTag,
            configuration.NeverTouchTag);
    }

    private static ActivationJournalDocument UpsertMarkerIntents(
        ActivationJournalDocument journal,
        IReadOnlyList<string> hashes,
        Dictionary<string, TorrentSnapshot> snapshots,
        string markerTag)
    {
        var entries = journal.Torrents.ToDictionary(entry => entry.Hash, StringComparer.Ordinal);
        foreach (var hash in hashes)
        {
            var markerStage = snapshots[hash].Tags.Contains(markerTag)
                ? JournalMutationStage.Confirmed
                : JournalMutationStage.IntentPersisted;
            if (!entries.TryGetValue(hash, out var entry))
            {
                entry = EmptyEntry(hash);
            }

            entries[hash] = entry with
            {
                MarkerAddStage = markerStage,
                StopStage = markerStage == JournalMutationStage.Confirmed
                    ? entry.StopStage
                    : JournalMutationStage.None,
                StartStage = JournalMutationStage.None,
                MarkerRemoveStage = JournalMutationStage.None,
            };
        }

        return WithEntriesIfChanged(journal, entries.Values);
    }

    private static ActivationJournalDocument ConfirmPresentMarkers(
        ActivationJournalDocument journal,
        Dictionary<string, TorrentSnapshot> snapshots,
        string markerTag)
    {
        return SetStage(
            journal,
            journal.Torrents
                .Where(entry => entry.MarkerAddStage == JournalMutationStage.IntentPersisted)
                .Where(entry => snapshots.TryGetValue(entry.Hash, out var torrent)
                    && torrent.Tags.Contains(markerTag))
                .Select(entry => entry.Hash),
            entry => entry with { MarkerAddStage = JournalMutationStage.Confirmed });
    }

    private static ActivationJournalDocument ConfirmAbsentMarkers(
        ActivationJournalDocument journal,
        Dictionary<string, TorrentSnapshot> snapshots,
        string markerTag)
    {
        return SetStage(
            journal,
            journal.Torrents
                .Where(entry => entry.MarkerRemoveStage == JournalMutationStage.IntentPersisted)
                .Where(entry => snapshots.TryGetValue(entry.Hash, out var torrent)
                    && !torrent.Tags.Contains(markerTag))
                .Select(entry => entry.Hash),
            entry => entry with { MarkerRemoveStage = JournalMutationStage.Confirmed });
    }

    private static ActivationJournalDocument ConfirmStoppedTorrents(
        ActivationJournalDocument journal,
        Dictionary<string, TorrentSnapshot> snapshots)
    {
        return SetStage(
            journal,
            journal.Torrents
                .Where(entry => entry.StopStage == JournalMutationStage.IntentPersisted)
                .Where(entry => snapshots.TryGetValue(entry.Hash, out var torrent)
                    && torrent.IsStopped)
                .Select(entry => entry.Hash),
            entry => entry with { StopStage = JournalMutationStage.Confirmed });
    }

    private static ActivationJournalDocument UpsertRestorationIntents(
        ActivationJournalDocument journal,
        IEnumerable<TorrentSnapshot> torrents,
        TorrentSelectionPolicy policy)
    {
        var entries = journal.Torrents.ToDictionary(entry => entry.Hash, StringComparer.Ordinal);
        foreach (var torrent in torrents
                     .Where(item => item.Tags.Contains(policy.MarkerTag))
                     .Where(item => !item.Tags.Contains(policy.NeverTouchTag)))
        {
            if (!entries.TryGetValue(torrent.Hash, out var entry))
            {
                entry = EmptyEntry(torrent.Hash);
            }

            entries[torrent.Hash] = entry with
            {
                MarkerAddStage = JournalMutationStage.Confirmed,
                StartStage = torrent.IsStopped
                    ? JournalMutationStage.IntentPersisted
                    : JournalMutationStage.Confirmed,
                MarkerRemoveStage = torrent.IsStopped
                    ? JournalMutationStage.None
                    : JournalMutationStage.IntentPersisted,
            };
        }

        return WithEntriesIfChanged(journal, entries.Values);
    }

    private static ActivationJournalDocument ConfirmStartsAndPlanMarkerRemoval(
        ActivationJournalDocument journal,
        IEnumerable<TorrentSnapshot> torrents,
        TorrentSelectionPolicy policy)
    {
        var confirmedHashes = torrents
            .Where(torrent => torrent.Tags.Contains(policy.MarkerTag))
            .Where(torrent => !torrent.Tags.Contains(policy.NeverTouchTag))
            .Where(torrent => !torrent.IsStopped)
            .Select(torrent => torrent.Hash);
        return SetStage(
            journal,
            confirmedHashes,
            entry => entry with
            {
                StartStage = JournalMutationStage.Confirmed,
                MarkerRemoveStage = JournalMutationStage.IntentPersisted,
            });
    }

    private static bool HasConfirmedMarker(
        ActivationJournalDocument journal,
        string hash)
    {
        return journal.Torrents.Any(entry =>
            string.Equals(entry.Hash, hash, StringComparison.Ordinal)
            && entry.MarkerAddStage == JournalMutationStage.Confirmed);
    }

    private static bool HasMarkerRemovalIntent(
        ActivationJournalDocument journal,
        string hash)
    {
        return journal.Torrents.Any(entry =>
            string.Equals(entry.Hash, hash, StringComparison.Ordinal)
            && entry.StartStage == JournalMutationStage.Confirmed
            && entry.MarkerRemoveStage == JournalMutationStage.IntentPersisted);
    }

    private static ActivationJournalDocument SetStage(
        ActivationJournalDocument journal,
        IEnumerable<string> hashes,
        Func<TorrentMutationJournalEntry, TorrentMutationJournalEntry> update)
    {
        var selected = hashes.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return journal;
        }

        var changed = false;
        var entries = journal.Torrents
            .Select(entry =>
            {
                if (!selected.Contains(entry.Hash))
                {
                    return entry;
                }

                var updated = update(entry);
                changed |= updated != entry;
                return updated;
            })
            .ToImmutableArray();
        return changed ? journal with { Torrents = entries } : journal;
    }

    private async Task<ActivationJournalDocument> PersistIfChangedAsync(
        ActivationJournalDocument previous,
        ActivationJournalDocument next,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(previous, next))
        {
            return previous;
        }

        await _journalStore.WriteAsync(next, cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static TorrentMutationJournalEntry EmptyEntry(string hash)
    {
        return new TorrentMutationJournalEntry(
            hash,
            JournalMutationStage.None,
            JournalMutationStage.None,
            JournalMutationStage.None,
            JournalMutationStage.None);
    }

    private static ImmutableArray<TorrentMutationJournalEntry> ToImmutableEntries(
        IEnumerable<TorrentMutationJournalEntry> entries)
    {
        return entries.OrderBy(entry => entry.Hash, StringComparer.Ordinal).ToImmutableArray();
    }

    private static ActivationJournalDocument WithEntriesIfChanged(
        ActivationJournalDocument journal,
        IEnumerable<TorrentMutationJournalEntry> entries)
    {
        var next = ToImmutableEntries(entries);
        return journal.Torrents.SequenceEqual(next)
            ? journal
            : journal with { Torrents = next };
    }
}
