using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Reads whether a configuration-stable activation currently exists.
/// </summary>
public interface IActivationStateReader
{
    /// <summary>Reads the current active or interrupted journal, if any.</summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The durable activation document or null.</returns>
    Task<ActivationJournalDocument?> ReadAsync(CancellationToken cancellationToken);
}
