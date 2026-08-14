using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Builds both independent actions against the activation-fixed endpoint and latest credential.
/// </summary>
public sealed class ConfiguredProtectionActionSet : IProtectionActionSet
{
    private readonly IQbittorrentClientFactory _clientFactory;
    private readonly IActivationJournalStore _journalStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredProtectionActionSet"/> class.
    /// </summary>
    /// <param name="clientFactory">The activation-pinned client factory.</param>
    /// <param name="journalStore">The shared durable mutation state.</param>
    public ConfiguredProtectionActionSet(
        IQbittorrentClientFactory clientFactory,
        IActivationJournalStore journalStore)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(journalStore);
        _clientFactory = clientFactory;
        _journalStore = journalStore;
    }

    /// <inheritdoc />
    public Task<ProtectionActionSetResult> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        return ReconcileAsync(journal, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProtectionActionSetResult> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken)
    {
        return ReconcileAsync(journal, authority, cancellationToken);
    }

    private async Task<ProtectionActionSetResult> ReconcileAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority? authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        try
        {
            var client = _clientFactory.Create(journal.Endpoint);
            using var alternativeLimits = new AlternativeLimitsActionService(client, _journalStore);
            using var stopTorrents = new StopTorrentsActionService(client, _journalStore);
            var actions = new ProtectionActionSet(
                alternativeLimits,
                stopTorrents,
                _journalStore);
            return authority.HasValue
                ? await actions
                    .ReconcileRestorationAsync(journal, authority.Value, cancellationToken)
                    .ConfigureAwait(false)
                : await actions
                    .ReconcileProtectionAsync(journal, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (QbittorrentClientException exception)
        {
            return new ProtectionActionSetResult(
                journal,
                false,
                QbittorrentFailureMapper.Map(exception.Error));
        }
    }
}
