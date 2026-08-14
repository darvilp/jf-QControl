using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Reconciles one authoritative playback snapshot with durable protection state.
/// </summary>
public interface IProtectionCoordinator
{
    /// <summary>
    /// Performs one serialized reconciliation.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The resulting privacy-safe lifecycle snapshot.</returns>
    Task<ProtectionCoordinatorSnapshot> ReconcileAsync(CancellationToken cancellationToken);
}
