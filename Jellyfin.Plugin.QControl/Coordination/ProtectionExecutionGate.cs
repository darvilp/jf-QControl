using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Semaphore-backed process-wide protection execution gate.
/// </summary>
public sealed class ProtectionExecutionGate : IProtectionExecutionGate, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
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
}
