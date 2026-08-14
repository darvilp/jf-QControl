using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// The durable activation-state boundary used by application services.
/// </summary>
public interface IActivationJournalStore
{
    /// <summary>
    /// Atomically replaces the durable activation journal.
    /// </summary>
    /// <param name="document">The complete new journal state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing durable replacement.</returns>
    ValueTask WriteAsync(
        ActivationJournalDocument document,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads and classifies the durable activation journal.
    /// </summary>
    /// <param name="currentProcessInstanceId">The current process identity.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The classified journal state.</returns>
    ValueTask<ActivationJournalLoadResult> LoadAsync(
        Guid currentProcessInstanceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the durable activation journal.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing deletion.</returns>
    ValueTask DeleteAsync(CancellationToken cancellationToken);
}
