using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Serializes automatic reconciliation, accepted configuration, and explicit recovery.
/// </summary>
public interface IProtectionExecutionGate
{
    /// <summary>Executes one operation with exclusive mutation authority.</summary>
    /// <typeparam name="T">The operation result type.</typeparam>
    /// <param name="operation">The operation to serialize.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The operation result.</returns>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}
