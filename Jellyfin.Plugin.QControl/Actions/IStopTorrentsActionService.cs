using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Actions;

/// <summary>
/// Reconciles the journaled Stop Torrents action without owning playback state.
/// </summary>
public interface IStopTorrentsActionService
{
    /// <summary>
    /// Acquires or reasserts protection for the journal's fixed configuration.
    /// </summary>
    /// <param name="journal">The current durable activation state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The latest state durably written by the action.</returns>
    Task<ActivationJournalDocument> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores marked torrents after normal release when the journal grants authority.
    /// </summary>
    /// <param name="journal">The current durable activation state.</param>
    /// <param name="authority">The authority derived while loading the journal.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The latest state durably written by the action.</returns>
    Task<ActivationJournalDocument> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken);
}
