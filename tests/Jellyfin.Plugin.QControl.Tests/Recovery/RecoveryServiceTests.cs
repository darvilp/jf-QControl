using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Jellyfin.Plugin.QControl.QBittorrent;
using Jellyfin.Plugin.QControl.Recovery;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Recovery;

public sealed class RecoveryServiceTests
{
    [Fact]
    public async Task ResumeMarkedWithoutJournalPersistsIntentAndHonorsNeverTouch()
    {
        var events = new List<string>();
        var store = new RecordingStore(events);
        var qbit = new RecoveryQbittorrentClient(events)
        {
            Torrents =
            [
                Torrent("a", true, "jfStopped"),
                Torrent("b", true, "jfStopped", "jfNeverTouch"),
            ],
        };
        using var gate = new ProtectionExecutionGate();
        var state = new RecordingStateControl();
        var service = CreateService(store, qbit, gate, state);

        var result = await service.ResumeMarkedTorrentsAsync(CancellationToken.None);

        Assert.Equal(RecoveryOutcome.Completed, result.Outcome);
        Assert.Equal(["a"], Assert.Single(qbit.StartCalls));
        Assert.Equal(["a"], Assert.Single(qbit.RemoveTagCalls));
        Assert.True(events.IndexOf("journal:write") < events.IndexOf("qbit:start"));
        Assert.Contains(qbit.Torrents, torrent =>
            torrent.Hash == "b"
            && torrent.IsStopped
            && torrent.Tags.Contains("jfStopped"));
        Assert.Null(store.Current);
        Assert.Equal(1, state.Invalidations);
    }

    [Fact]
    public async Task RestorePreviousSpeedPersistsIntentAndRetriesAfterFailure()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { Current = RecoveryJournal(initialEnabled: false) };
        var qbit = new RecoveryQbittorrentClient(events)
        {
            AlternativeLimitsEnabled = true,
            SetFailure = new QbittorrentClientException(
                QbittorrentClientError.Connection,
                "Endpoint unavailable."),
        };
        using var gate = new ProtectionExecutionGate();
        var service = CreateService(store, qbit, gate, new RecordingStateControl());

        var failed = await service.RestorePreviousSpeedSettingAsync(CancellationToken.None);

        Assert.Equal(RecoveryOutcome.Failed, failed.Outcome);
        Assert.Equal(JournalFailureCode.Connection, failed.Failure);
        Assert.Equal(JournalMutationStage.IntentPersisted,
            store.Current?.AlternativeLimits.ManualRestoreStage);
        Assert.True(events.IndexOf("journal:write") < events.IndexOf("qbit:set-limits"));

        qbit.SetFailure = null;
        var retried = await service.RestorePreviousSpeedSettingAsync(CancellationToken.None);

        Assert.Equal(RecoveryOutcome.Completed, retried.Outcome);
        Assert.False(qbit.AlternativeLimitsEnabled);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task RestorePreviousEnabledStateDoesNotDependOnActivationOwnership()
    {
        var events = new List<string>();
        var journal = RecoveryJournal(initialEnabled: true) with
        {
            AlternativeLimits = new AlternativeLimitsJournalState(
                true,
                false,
                JournalMutationStage.Confirmed,
                JournalMutationStage.None),
        };
        var store = new RecordingStore(events) { Current = journal };
        var qbit = new RecoveryQbittorrentClient(events)
        {
            AlternativeLimitsEnabled = false,
        };
        using var gate = new ProtectionExecutionGate();
        var service = CreateService(store, qbit, gate, new RecordingStateControl());

        var result = await service.RestorePreviousSpeedSettingAsync(CancellationToken.None);

        Assert.Equal(RecoveryOutcome.Completed, result.Outcome);
        Assert.True(qbit.AlternativeLimitsEnabled);
        Assert.Equal([true], qbit.LimitsSetCalls);
    }

    [Fact]
    public async Task MarkResolvedDeletesJournalWithoutQbittorrentCall()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { Current = RecoveryJournal(initialEnabled: false) };
        var qbit = new RecoveryQbittorrentClient(events);
        using var gate = new ProtectionExecutionGate();
        var state = new RecordingStateControl();
        var service = CreateService(store, qbit, gate, state);

        var result = await service.MarkResolvedAsync(CancellationToken.None);

        Assert.Equal(RecoveryOutcome.Completed, result.Outcome);
        Assert.Null(store.Current);
        Assert.Equal(0, qbit.CallCount);
        Assert.Equal(["journal:load", "journal:delete"], events);
        Assert.Equal(1, state.Invalidations);
    }

    private static RecoveryService CreateService(
        IActivationJournalStore store,
        IQbittorrentClient qbit,
        IProtectionExecutionGate gate,
        IProtectionCoordinatorStateControl state)
    {
        return new RecoveryService(
            store,
            new ProcessInstanceIdentity(Guid.NewGuid()),
            new StaticPersistence(CurrentConfiguration()),
            new StaticClientFactory(qbit),
            gate,
            state,
            new RecordingWakeSignal(),
            new StaticTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)));
    }

    private static PluginConfiguration CurrentConfiguration()
    {
        return new PluginConfiguration
        {
            Revision = 4,
            QbittorrentBaseAddress = "http://qbit:8080",
            CredentialMode = QbittorrentCredentialMode.StoredApiKey,
            QbittorrentApiKey = "qbt_1234567890123456789012345678",
            ConnectionValidated = true,
            StopScope = TorrentScope.All,
            IncludeIncomplete = true,
            IncludeCompleted = true,
            MarkerTag = "jfStopped",
            NeverTouchTag = "jfNeverTouch",
            ReleaseGraceSeconds = 60,
        };
    }

    private static ActivationJournalDocument RecoveryJournal(bool initialEnabled)
    {
        return new ActivationJournalDocument(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero),
            ImmutableArray.Create("session-a"),
            new JournalConfigurationSnapshot(
                3,
                true,
                false,
                TorrentScope.All,
                [],
                true,
                true,
                "jfStopped",
                "jfNeverTouch",
                TimeSpan.FromSeconds(60)),
            new QbittorrentEndpointIdentity("http", "qbit", 8080, "/"),
            new AlternativeLimitsJournalState(
                initialEnabled,
                !initialEnabled,
                JournalMutationStage.Confirmed,
                JournalMutationStage.None),
            [],
            ProtectionPhase.Protecting,
            null,
            null,
            null);
    }

    private static TorrentSnapshot Torrent(string hash, bool stopped, params string[] tags) =>
        new(hash, null, 0, stopped, tags);

    private sealed class RecordingStore(List<string> events) : IActivationJournalStore
    {
        public ActivationJournalDocument? Current { get; set; }

        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken)
        {
            events.Add("journal:load");
            return ValueTask.FromResult(Current is null
                ? new ActivationJournalLoadResult(
                    ActivationJournalLoadStatus.Missing,
                    ActivationJournalAuthority.None,
                    null)
                : new ActivationJournalLoadResult(
                    ActivationJournalLoadStatus.Interrupted,
                    ActivationJournalAuthority.ProtectOnly,
                    Current));
        }

        public ValueTask WriteAsync(
            ActivationJournalDocument document,
            CancellationToken cancellationToken)
        {
            events.Add("journal:write");
            Current = document;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            events.Add("journal:delete");
            Current = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticPersistence(PluginConfiguration configuration)
        : IPluginConfigurationPersistence
    {
        public PluginConfiguration Current => configuration;

        public void Save(PluginConfiguration next) => throw new NotSupportedException();
    }

    private sealed class StaticClientFactory(IQbittorrentClient client)
        : IQbittorrentClientFactory
    {
        public IQbittorrentClient Create(QbittorrentEndpointIdentity endpoint) => client;

        public IQbittorrentClient Create(PluginConfiguration configuration) => client;
    }

    private sealed class RecordingStateControl : IProtectionCoordinatorStateControl
    {
        public int Invalidations { get; private set; }

        public void InvalidateJournalCache()
        {
            Invalidations++;
        }
    }

    private sealed class RecordingWakeSignal : IProtectionWakeSignal
    {
        public void Wake()
        {
        }
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecoveryQbittorrentClient(List<string> events) : IQbittorrentClient
    {
        public IReadOnlyList<TorrentSnapshot> Torrents { get; set; } = [];

        public bool AlternativeLimitsEnabled { get; set; }

        public QbittorrentClientException? SetFailure { get; set; }

        public List<IReadOnlyList<string>> StartCalls { get; } = [];

        public List<IReadOnlyList<string>> RemoveTagCalls { get; } = [];

        public List<bool> LimitsSetCalls { get; } = [];

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<TorrentSnapshot>> GetTorrentsAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Torrents);
        }

        public Task StartTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken)
        {
            CallCount++;
            events.Add("qbit:start");
            var copied = hashes.ToArray();
            StartCalls.Add(copied);
            Torrents = Torrents.Select(torrent => copied.Contains(torrent.Hash, StringComparer.Ordinal)
                ? new TorrentSnapshot(
                    torrent.Hash,
                    torrent.Category,
                    torrent.RemainingBytes,
                    false,
                    torrent.Tags)
                : torrent).ToArray();
            return Task.CompletedTask;
        }

        public Task RemoveTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken)
        {
            CallCount++;
            events.Add("qbit:remove-tag");
            var copied = hashes.ToArray();
            RemoveTagCalls.Add(copied);
            Torrents = Torrents.Select(torrent => copied.Contains(torrent.Hash, StringComparer.Ordinal)
                ? new TorrentSnapshot(
                    torrent.Hash,
                    torrent.Category,
                    torrent.RemainingBytes,
                    torrent.IsStopped,
                    torrent.Tags.Where(value => !string.Equals(value, tag, StringComparison.Ordinal)))
                : torrent).ToArray();
            return Task.CompletedTask;
        }

        public Task<bool> GetAlternativeLimitsEnabledAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(AlternativeLimitsEnabled);
        }

        public Task SetAlternativeLimitsEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            CallCount++;
            events.Add("qbit:set-limits");
            LimitsSetCalls.Add(enabled);
            if (SetFailure is not null)
            {
                throw SetFailure;
            }

            AlternativeLimitsEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task<QbittorrentServerInfo> GetServerInfoAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
