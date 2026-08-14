using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Coordination;

public sealed class ProtectionCoordinatorTests
{
    private static readonly Guid ProcessId =
        new("1c7e3a91-2529-4b04-8859-bac7f9d68542");

    [Fact]
    public async Task PausedPlaybackCreatesJournalBeforeProtectionAction()
    {
        var events = new List<string>();
        var sessions = new MutableSessionSource(events)
        {
            Sessions = [new PlaybackSessionSnapshot("paused-session", true, true)],
        };
        var store = new RecordingJournalStore(events);
        var actions = new RecordingProtectionActions(events);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        using var coordinator = new ProtectionCoordinator(
            sessions,
            new TestActivationJournalFactory(),
            actions,
            store,
            clock,
            ProcessId);

        var result = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(["journal:load", "sessions:read", "journal:write", "actions:protect", "journal:write"], events);
        Assert.Equal(ProtectionPhase.Protecting, result.Phase);
        Assert.Equal(["paused-session"], result.SessionIds);
        Assert.Equal(1, actions.ProtectionCalls);
        Assert.NotNull(store.Current);
        Assert.Equal(ProcessId, store.Current.ProcessInstanceId);
    }

    [Fact]
    public async Task OverlappingSessionsRequireFullGraceAndPresenceCancelsRelease()
    {
        var events = new List<string>();
        var sessions = new MutableSessionSource(events)
        {
            Sessions =
            [
                new PlaybackSessionSnapshot("a", true, false),
                new PlaybackSessionSnapshot("b", true, true),
            ],
        };
        var store = new RecordingJournalStore(events);
        var actions = new RecordingProtectionActions(events) { RestorationSettled = true };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        using var coordinator = new ProtectionCoordinator(
            sessions,
            new TestActivationJournalFactory(releaseGrace: TimeSpan.FromSeconds(60)),
            actions,
            store,
            clock,
            ProcessId);
        _ = await coordinator.ReconcileAsync(CancellationToken.None);

        sessions.Sessions = [new PlaybackSessionSnapshot("b", true, true)];
        var oneRemaining = await coordinator.ReconcileAsync(CancellationToken.None);
        Assert.Equal(ProtectionPhase.Protecting, oneRemaining.Phase);
        Assert.Equal(0, actions.RestorationCalls);

        sessions.Sessions = [];
        var pending = await coordinator.ReconcileAsync(CancellationToken.None);
        Assert.Equal(ProtectionPhase.ReleasePending, pending.Phase);
        Assert.Equal(clock.GetUtcNow().AddSeconds(60), pending.ReleaseDueAt);

        clock.Advance(TimeSpan.FromSeconds(59));
        var stillPending = await coordinator.ReconcileAsync(CancellationToken.None);
        Assert.Equal(ProtectionPhase.ReleasePending, stillPending.Phase);
        Assert.Equal(0, actions.RestorationCalls);

        sessions.Sessions = [new PlaybackSessionSnapshot("episode-two", true, false)];
        var cancelled = await coordinator.ReconcileAsync(CancellationToken.None);
        Assert.Equal(ProtectionPhase.Protecting, cancelled.Phase);
        Assert.Null(cancelled.ReleaseDueAt);

        sessions.Sessions = [];
        _ = await coordinator.ReconcileAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(60));
        var released = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(ProtectionPhase.Inactive, released.Phase);
        Assert.Equal(1, actions.RestorationCalls);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task InterruptedJournalProtectsOnPresenceButNeverAutomaticallyRestores()
    {
        var events = new List<string>();
        var interrupted = TestActivationJournalFactory.CreateDocument(
            processId: Guid.NewGuid(),
            new PlaybackPresenceSnapshot(true, ["old-session"]),
            new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero),
            TimeSpan.Zero);
        var sessions = new MutableSessionSource(events) { Sessions = [] };
        var store = new RecordingJournalStore(events)
        {
            LoadResult = new ActivationJournalLoadResult(
                ActivationJournalLoadStatus.Interrupted,
                ActivationJournalAuthority.ProtectOnly,
                interrupted),
            Current = interrupted,
        };
        var actions = new RecordingProtectionActions(events) { RestorationSettled = true };
        using var coordinator = new ProtectionCoordinator(
            sessions,
            new TestActivationJournalFactory(releaseGrace: TimeSpan.Zero),
            actions,
            store,
            new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
            ProcessId);

        var absent = await coordinator.ReconcileAsync(CancellationToken.None);
        Assert.True(absent.RecoveryRequired);
        Assert.Equal(0, actions.RestorationCalls);
        Assert.Equal(0, store.DeleteCalls);

        sessions.Sessions = [new PlaybackSessionSnapshot("new-session", true, false)];
        var present = await coordinator.ReconcileAsync(CancellationToken.None);
        Assert.True(present.RecoveryRequired);
        Assert.Equal(1, actions.ProtectionCalls);
        Assert.Equal(0, actions.RestorationCalls);
    }

    [Theory]
    [InlineData(ProtectionPhase.Protecting)]
    [InlineData(ProtectionPhase.ReleasePending)]
    [InlineData(ProtectionPhase.Restoring)]
    public async Task InterruptedJournalNeverRestoresFromAnyActiveLifecyclePhase(
        ProtectionPhase phase)
    {
        var events = new List<string>();
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var interrupted = TestActivationJournalFactory.CreateDocument(
            processId: Guid.NewGuid(),
            new PlaybackPresenceSnapshot(true, ["old-session"]),
            now.AddHours(-1),
            TimeSpan.FromSeconds(60)) with
        {
            Phase = phase,
            ReleaseDueAt = phase == ProtectionPhase.ReleasePending
                ? now.AddSeconds(-1)
                : null,
        };
        var store = new RecordingJournalStore(events)
        {
            LoadResult = new ActivationJournalLoadResult(
                ActivationJournalLoadStatus.Interrupted,
                ActivationJournalAuthority.ProtectOnly,
                interrupted),
            Current = interrupted,
        };
        var actions = new RecordingProtectionActions(events) { RestorationSettled = true };
        using var coordinator = new ProtectionCoordinator(
            new MutableSessionSource(events) { Sessions = [] },
            new TestActivationJournalFactory(),
            actions,
            store,
            new ManualTimeProvider(now),
            ProcessId);

        var result = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.True(result.RecoveryRequired);
        Assert.Equal(0, actions.RestorationCalls);
        Assert.Equal(0, store.DeleteCalls);
        Assert.Same(interrupted, store.Current);
    }

    [Fact]
    public async Task ZeroGraceRestoresOnFirstAuthoritativeAbsentSnapshot()
    {
        var events = new List<string>();
        var sessions = new MutableSessionSource(events)
        {
            Sessions = [new PlaybackSessionSnapshot("a", true, false)],
        };
        var store = new RecordingJournalStore(events);
        var actions = new RecordingProtectionActions(events) { RestorationSettled = true };
        using var coordinator = new ProtectionCoordinator(
            sessions,
            new TestActivationJournalFactory(releaseGrace: TimeSpan.Zero),
            actions,
            store,
            new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
            ProcessId);
        _ = await coordinator.ReconcileAsync(CancellationToken.None);
        sessions.Sessions = [];

        var result = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(ProtectionPhase.Inactive, result.Phase);
        Assert.Equal(1, actions.RestorationCalls);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public async Task ActionFailureDoesNotChangeAuthoritativePlaybackPresence()
    {
        var events = new List<string>();
        var sessions = new MutableSessionSource(events)
        {
            Sessions = [new PlaybackSessionSnapshot("a", true, false)],
        };
        var store = new RecordingJournalStore(events);
        var actions = new RecordingProtectionActions(events)
        {
            Failure = JournalFailureCode.Connection,
        };
        using var coordinator = new ProtectionCoordinator(
            sessions,
            new TestActivationJournalFactory(),
            actions,
            store,
            new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
            ProcessId);

        var result = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(ProtectionPhase.Protecting, result.Phase);
        Assert.Equal(JournalFailureCode.Connection, store.Current?.LastFailure);
        Assert.Equal(0, actions.RestorationCalls);
    }

    [Fact]
    public async Task ConcurrentWakeReconciliationIsSerialized()
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new MutableSessionSource(events)
        {
            Sessions = [new PlaybackSessionSnapshot("a", true, false)],
            FirstReadEntered = entered,
            FirstReadRelease = release.Task,
        };
        using var coordinator = new ProtectionCoordinator(
            sessions,
            new TestActivationJournalFactory(),
            new RecordingProtectionActions(events),
            new RecordingJournalStore(events),
            new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
            ProcessId);

        var first = coordinator.ReconcileAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = coordinator.ReconcileAsync(CancellationToken.None);
        Assert.Equal(1, sessions.ReadCalls);

        release.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(2, sessions.ReadCalls);
    }

    private sealed class MutableSessionSource(List<string> events) : IPlaybackSessionSource
    {
        public IReadOnlyList<PlaybackSessionSnapshot> Sessions { get; set; } = [];

        public TaskCompletionSource<bool>? FirstReadEntered { get; set; }

        public Task? FirstReadRelease { get; set; }

        public int ReadCalls { get; private set; }

        public async Task<IReadOnlyList<PlaybackSessionSnapshot>> ReadAsync(
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            events.Add("sessions:read");
            if (ReadCalls == 1 && FirstReadRelease is not null)
            {
                FirstReadEntered?.SetResult(true);
                await FirstReadRelease.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return Sessions;
        }
    }

    private sealed class TestActivationJournalFactory(
        TimeSpan? releaseGrace = null) : IActivationJournalFactory
    {
        private readonly TimeSpan _releaseGrace = releaseGrace ?? TimeSpan.FromSeconds(60);

        public ActivationJournalDocument? Create(
            PlaybackPresenceSnapshot presence,
            Guid processInstanceId,
            DateTimeOffset now)
        {
            return CreateDocument(processInstanceId, presence, now, _releaseGrace);
        }

        public static ActivationJournalDocument CreateDocument(
            Guid processId,
            PlaybackPresenceSnapshot presence,
            DateTimeOffset now,
            TimeSpan releaseGrace)
        {
            return new ActivationJournalDocument(
                SchemaVersion: 1,
                ProcessInstanceId: processId,
                ActivationId: new Guid("70bf4b07-cdad-4a69-827f-81f0251d3eb0"),
                StartedAt: now,
                SessionIds: presence.SessionIds.ToImmutableArray(),
                Configuration: new JournalConfigurationSnapshot(
                    Revision: 1,
                    AlternativeLimitsEnabled: false,
                    StopTorrentsEnabled: true,
                    StopScope: TorrentScope.All,
                    SelectedCategories: [],
                    IncludeIncomplete: true,
                    IncludeCompleted: true,
                    MarkerTag: "jfStopped",
                    NeverTouchTag: "jfNeverTouch",
                    ReleaseGrace: releaseGrace),
                Endpoint: new QbittorrentEndpointIdentity("http", "qbittorrent", 8080, "/"),
                AlternativeLimits: new AlternativeLimitsJournalState(
                    InitialEnabled: null,
                    EnabledByActivation: false,
                    EnableStage: JournalMutationStage.None,
                    DisableStage: JournalMutationStage.None),
                Torrents: [],
                Phase: ProtectionPhase.Protecting,
                ReleaseDueAt: null,
                LastSuccessfulReconciliation: null,
                LastFailure: null);
        }
    }

    private sealed class RecordingProtectionActions(List<string> events) : IProtectionActionSet
    {
        public int ProtectionCalls { get; private set; }

        public int RestorationCalls { get; private set; }

        public bool RestorationSettled { get; set; }

        public JournalFailureCode? Failure { get; set; }

        public Task<ProtectionActionSetResult> ReconcileProtectionAsync(
            ActivationJournalDocument journal,
            CancellationToken cancellationToken)
        {
            ProtectionCalls++;
            events.Add("actions:protect");
            return Task.FromResult(new ProtectionActionSetResult(journal, false, Failure));
        }

        public Task<ProtectionActionSetResult> ReconcileRestorationAsync(
            ActivationJournalDocument journal,
            ActivationJournalAuthority authority,
            CancellationToken cancellationToken)
        {
            RestorationCalls++;
            events.Add("actions:restore");
            return Task.FromResult(new ProtectionActionSetResult(
                journal,
                RestorationSettled,
                Failure));
        }
    }

    private sealed class RecordingJournalStore(List<string> events) : IActivationJournalStore
    {
        public ActivationJournalDocument? Current { get; set; }

        public ActivationJournalLoadResult? LoadResult { get; set; }

        public int DeleteCalls { get; private set; }

        public ValueTask WriteAsync(
            ActivationJournalDocument document,
            CancellationToken cancellationToken)
        {
            events.Add("journal:write");
            Current = document;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken)
        {
            events.Add("journal:load");
            return ValueTask.FromResult(LoadResult ?? new ActivationJournalLoadResult(
                ActivationJournalLoadStatus.Missing,
                ActivationJournalAuthority.None,
                null));
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            events.Add("journal:delete");
            DeleteCalls++;
            Current = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount)
        {
            _now += amount;
        }
    }
}
