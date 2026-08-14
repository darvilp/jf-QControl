using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Implements coordinator waits using the runtime clock.
/// </summary>
public sealed class TimeProviderReconciliationDelay : IReconciliationDelay
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeProviderReconciliationDelay"/> class.
    /// </summary>
    /// <param name="timeProvider">The runtime clock.</param>
    public TimeProviderReconciliationDelay(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, _timeProvider, cancellationToken);
    }
}
