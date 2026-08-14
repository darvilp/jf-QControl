using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Coordination;

public sealed class ProtectionCoordinatorWorkerTests
{
    [Fact]
    public async Task StartsImmediatelyAndPeriodicallyRepairsMissedInactiveEvents()
    {
        var coordinator = new ControllableCoordinator(Inactive());
        var delay = new ControllableDelay();
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        using var worker = CreateWorker(coordinator, delay, clock);

        await worker.StartAsync(CancellationToken.None);
        await coordinator.WaitForCallsAsync(1);
        var firstDelay = await delay.NextAsync();

        Assert.Equal(TimeSpan.FromSeconds(10), firstDelay.Duration);

        firstDelay.Complete();
        await coordinator.WaitForCallsAsync(2);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EventWakeupsCoalesceWhileReconciliationIsRunning()
    {
        var coordinator = new ControllableCoordinator(Protecting());
        var firstCallRelease = coordinator.BlockNextCall();
        var delay = new ControllableDelay();
        using var worker = CreateWorker(
            coordinator,
            delay,
            new ManualTimeProvider(Utc(12, 0, 0)));

        await worker.StartAsync(CancellationToken.None);
        await coordinator.WaitForCallsAsync(1);

        worker.Wake();
        worker.Wake();
        worker.Wake();
        firstCallRelease.SetResult(true);

        await coordinator.WaitForCallsAsync(2);
        var activeDelay = await delay.NextAsync();
        Assert.Equal(TimeSpan.FromSeconds(15), activeDelay.Duration);
        Assert.Equal(2, coordinator.CallCount);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReleasePendingSchedulesExactDeadlineBeforePeriodicInterval()
    {
        var now = Utc(12, 0, 0);
        var coordinator = new ControllableCoordinator(new ProtectionCoordinatorSnapshot(
            ProtectionPhase.ReleasePending,
            ["session-a"],
            now.AddSeconds(7),
            false));
        var delay = new ControllableDelay();
        using var worker = CreateWorker(
            coordinator,
            delay,
            new ManualTimeProvider(now));

        await worker.StartAsync(CancellationToken.None);
        await coordinator.WaitForCallsAsync(1);
        var deadlineDelay = await delay.NextAsync();

        Assert.Equal(TimeSpan.FromSeconds(7), deadlineDelay.Duration);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailureRetriesAndShutdownCancelsPendingDelay()
    {
        var coordinator = new ControllableCoordinator(Inactive())
        {
            NextException = new InvalidOperationException("fixture failure"),
        };
        var delay = new ControllableDelay();
        using var worker = CreateWorker(
            coordinator,
            delay,
            new ManualTimeProvider(Utc(12, 0, 0)));

        await worker.StartAsync(CancellationToken.None);
        await coordinator.WaitForCallsAsync(1);
        var retryDelay = await delay.NextAsync();
        Assert.Equal(TimeSpan.FromSeconds(15), retryDelay.Duration);

        retryDelay.Complete();
        await coordinator.WaitForCallsAsync(2);
        var periodicDelay = await delay.NextAsync();
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => periodicDelay.Completion);
    }

    private static ProtectionCoordinatorWorker CreateWorker(
        IProtectionCoordinator coordinator,
        IReconciliationDelay delay,
        TimeProvider timeProvider)
    {
        return new ProtectionCoordinatorWorker(
            coordinator,
            delay,
            timeProvider,
            NullLogger<ProtectionCoordinatorWorker>.Instance);
    }

    private static ProtectionCoordinatorSnapshot Inactive() => new(
        ProtectionPhase.Inactive,
        [],
        null,
        false);

    private static ProtectionCoordinatorSnapshot Protecting() => new(
        ProtectionPhase.Protecting,
        ["session-a"],
        null,
        false);

    private static DateTimeOffset Utc(int hour, int minute, int second) =>
        new(2026, 8, 14, hour, minute, second, TimeSpan.Zero);

    private sealed class ControllableCoordinator(ProtectionCoordinatorSnapshot snapshot)
        : IProtectionCoordinator
    {
        private readonly Channel<int> _calls = Channel.CreateUnbounded<int>();
        private TaskCompletionSource<bool>? _nextCallRelease;
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Exception? NextException { get; set; }

        public TaskCompletionSource<bool> BlockNextCall()
        {
            _nextCallRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _nextCallRelease;
        }

        public async Task<ProtectionCoordinatorSnapshot> ReconcileAsync(
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            _calls.Writer.TryWrite(call);
            if (_nextCallRelease is not null)
            {
                var release = _nextCallRelease;
                _nextCallRelease = null;
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (NextException is not null)
            {
                var exception = NextException;
                NextException = null;
                throw exception;
            }

            return snapshot;
        }

        public async Task WaitForCallsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            while (CallCount < count)
            {
                _ = await _calls.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            }
        }
    }

    private sealed class ControllableDelay : IReconciliationDelay
    {
        private readonly Channel<DelayRequest> _requests =
            Channel.CreateUnbounded<DelayRequest>();

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var request = new DelayRequest(delay);
            _requests.Writer.TryWrite(request);
            return request.WaitAsync(cancellationToken);
        }

        public async Task<DelayRequest> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            return await _requests.Reader
                .ReadAsync(timeout.Token)
                .ConfigureAwait(false);
        }
    }

    private sealed class DelayRequest(TimeSpan duration)
    {
        private readonly TaskCompletionSource<bool> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TimeSpan Duration { get; } = duration;

        public Task Completion => _completion.Task;

        public void Complete() => _completion.TrySetResult(true);

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                () => _completion.TrySetCanceled(cancellationToken));
            await _completion.Task.ConfigureAwait(false);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
