using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Waits between coordinator passes without coupling tests to wall-clock time.
/// </summary>
public interface IReconciliationDelay
{
    /// <summary>
    /// Waits for the requested interval or cancellation.
    /// </summary>
    /// <param name="delay">The non-negative interval.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing the wait.</returns>
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}
