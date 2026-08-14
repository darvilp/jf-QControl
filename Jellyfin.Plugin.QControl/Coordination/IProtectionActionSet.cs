using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Reconciles every independently enabled qBittorrent protection action.
/// </summary>
public interface IProtectionActionSet
{
    /// <summary>
    /// Acquires or reasserts every enabled action.
    /// </summary>
    /// <param name="journal">The current durable activation state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The latest durable document and bounded outcome.</returns>
    Task<ProtectionActionSetResult> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores every enabled action allowed by journal authority.
    /// </summary>
    /// <param name="journal">The current durable activation state.</param>
    /// <param name="authority">The loaded automatic mutation authority.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The latest durable document and bounded outcome.</returns>
    Task<ProtectionActionSetResult> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken);
}
