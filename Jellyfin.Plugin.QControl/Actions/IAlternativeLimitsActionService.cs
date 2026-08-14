using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Actions;

/// <summary>
/// Reconciles activation-owned Alternative Limits state without configuring rate values.
/// </summary>
public interface IAlternativeLimitsActionService
{
    /// <summary>
    /// Observes ownership and enforces enabled mode for protection.
    /// </summary>
    /// <param name="journal">The current durable activation state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The latest state durably written by the action.</returns>
    Task<ActivationJournalDocument> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores only an enabled transition owned by this uninterrupted activation.
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
