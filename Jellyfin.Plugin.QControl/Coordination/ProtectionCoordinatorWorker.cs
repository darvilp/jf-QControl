using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Runs startup, event-driven, periodic, and grace-deadline reconciliation.
/// </summary>
public sealed class ProtectionCoordinatorWorker : BackgroundService, IProtectionWakeSignal
{
    private static readonly TimeSpan InactiveInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(15);
    private static readonly Action<ILogger, string, Exception?> LogReconciliationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(LogReconciliationFailure)),
            "QControl reconciliation failed with {FailureType}; retrying.");

    private readonly IProtectionCoordinator _coordinator;
    private readonly IReconciliationDelay _delay;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProtectionCoordinatorWorker> _logger;
    private readonly Channel<bool> _wakeups = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectionCoordinatorWorker"/> class.
    /// </summary>
    /// <param name="coordinator">The serialized coordinator boundary.</param>
    /// <param name="delay">The cancellable scheduling boundary.</param>
    /// <param name="timeProvider">The runtime clock.</param>
    /// <param name="logger">The privacy-safe runtime logger.</param>
    public ProtectionCoordinatorWorker(
        IProtectionCoordinator coordinator,
        IReconciliationDelay delay,
        TimeProvider timeProvider,
        ILogger<ProtectionCoordinatorWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _coordinator = coordinator;
        _delay = delay;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Wake()
    {
        _wakeups.Writer.TryWrite(true);
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A hosted coordinator must retry unexpected failures instead of stopping Jellyfin.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelay = ActiveInterval;
            try
            {
                var snapshot = await _coordinator
                    .ReconcileAsync(stoppingToken)
                    .ConfigureAwait(false);
                nextDelay = CalculateDelay(snapshot, _timeProvider.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReconciliationFailure(_logger, exception.GetType().Name, null);
            }

            await WaitForWakeOrDelayAsync(nextDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForWakeOrDelayAsync(
        TimeSpan delay,
        CancellationToken stoppingToken)
    {
        using var iterationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken);
        var wakeTask = _wakeups.Reader
            .WaitToReadAsync(iterationCancellation.Token)
            .AsTask();
        var delayTask = _delay.WaitAsync(delay, iterationCancellation.Token);

        try
        {
            var completed = await Task.WhenAny(wakeTask, delayTask).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            if (completed == wakeTask)
            {
                while (_wakeups.Reader.TryRead(out _))
                {
                }
            }
        }
        finally
        {
            await iterationCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private static TimeSpan CalculateDelay(
        ProtectionCoordinatorSnapshot snapshot,
        DateTimeOffset now)
    {
        if (snapshot.Phase == ProtectionPhase.Inactive)
        {
            return InactiveInterval;
        }

        if (snapshot.Phase != ProtectionPhase.ReleasePending
            || snapshot.ReleaseDueAt is null)
        {
            return ActiveInterval;
        }

        var untilRelease = snapshot.ReleaseDueAt.Value - now;
        if (untilRelease <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return untilRelease < ActiveInterval ? untilRelease : ActiveInterval;
    }
}
