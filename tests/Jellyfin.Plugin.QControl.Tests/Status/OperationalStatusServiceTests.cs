using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Jellyfin.Plugin.QControl.QBittorrent;
using Jellyfin.Plugin.QControl.Status;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Status;

public sealed class OperationalStatusServiceTests
{
    [Fact]
    public async Task InterruptedStatusReportsCountsAndPlaybackWithoutDisplayData()
    {
        var configuration = Configuration(revision: 8);
        var journal = Journal(revision: 7);
        var sessions = new StaticSessionSource(
        [
            new PlaybackSessionSnapshot("paused", true, true),
            new PlaybackSessionSnapshot("connected", false, false),
        ]);
        var qbit = new StatusQbittorrentClient
        {
            Torrents =
            [
                Torrent("a", stopped: false, amountLeft: 5),
                Torrent("b", stopped: true, amountLeft: 0, "jfStopped"),
                Torrent("c", stopped: false, amountLeft: 0, "manual"),
            ],
            AlternativeLimitsEnabled = true,
        };
        var service = CreateService(configuration, journal, sessions, qbit);

        var status = await service.GetAsync(CancellationToken.None);
        var shape = status.ToString();

        Assert.Equal(QbittorrentConnectivity.Connected, status.Connectivity);
        Assert.Equal("5.2.3", status.ApplicationVersion);
        Assert.Equal(OperationalProtectionState.RecoveryRequired, status.ProtectionState);
        Assert.Equal(1, status.QualifyingSessionCount);
        Assert.Equal(1, status.EligibleTorrentCount);
        Assert.Equal(1, status.MarkedTorrentCount);
        Assert.Equal(1, status.StoppedMarkedTorrentCount);
        Assert.Equal(1, status.ExcludedTorrentCount);
        Assert.True(status.AlternativeLimitsOwned);
        Assert.True(status.ConfigurationChangesPending);
        Assert.True(status.CanResumeMarkedTorrents);
        Assert.True(status.CanRestorePreviousSpeedSetting);
        Assert.DoesNotContain("private-user", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("private-device", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("private-title", shape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QbittorrentFailureDoesNotChangePlaybackOrRecoveryTruth()
    {
        var qbit = new StatusQbittorrentClient
        {
            Failure = new QbittorrentClientException(
                QbittorrentClientError.Connection,
                "Endpoint unavailable."),
        };
        var service = CreateService(
            Configuration(revision: 7),
            Journal(revision: 7),
            new StaticSessionSource([new PlaybackSessionSnapshot("paused", true, true)]),
            qbit);

        var status = await service.GetAsync(CancellationToken.None);

        Assert.Equal(QbittorrentConnectivity.Failed, status.Connectivity);
        Assert.Equal(JournalFailureCode.Connection, status.CurrentError);
        Assert.Equal(OperationalProtectionState.RecoveryRequired, status.ProtectionState);
        Assert.Equal(1, status.QualifyingSessionCount);
        Assert.Null(status.EligibleTorrentCount);
    }

    [Fact]
    public async Task MarkerWithoutJournalRemainsAvailableForExplicitRecovery()
    {
        var qbit = new StatusQbittorrentClient
        {
            Torrents = [Torrent("marked", stopped: true, amountLeft: 0, "jfStopped")],
        };
        var service = new OperationalStatusService(
            new StaticSessionSource([]),
            new EmptyJournalStore(),
            new ProcessInstanceIdentity(Guid.NewGuid()),
            new StaticPersistence(Configuration(revision: 3)),
            new StaticClientFactory(qbit),
            new StaticTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)));

        var status = await service.GetAsync(CancellationToken.None);

        Assert.Equal(OperationalProtectionState.Inactive, status.ProtectionState);
        Assert.Equal(1, status.MarkedTorrentCount);
        Assert.True(status.CanResumeMarkedTorrents);
    }

    [Fact]
    public async Task NeverTouchMarkerWithoutJournalIsVisibleButNotRecoverable()
    {
        var qbit = new StatusQbittorrentClient
        {
            Torrents =
            [
                Torrent(
                    "excluded-marked",
                    stopped: true,
                    amountLeft: 0,
                    "jfStopped",
                    "jfNeverTouch"),
            ],
        };
        var service = new OperationalStatusService(
            new StaticSessionSource([]),
            new EmptyJournalStore(),
            new ProcessInstanceIdentity(Guid.NewGuid()),
            new StaticPersistence(Configuration(revision: 3)),
            new StaticClientFactory(qbit),
            new StaticTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)));

        var status = await service.GetAsync(CancellationToken.None);

        Assert.Equal(1, status.MarkedTorrentCount);
        Assert.Equal(1, status.ExcludedTorrentCount);
        Assert.False(status.CanResumeMarkedTorrents);
    }

    private static OperationalStatusService CreateService(
        PluginConfiguration configuration,
        ActivationJournalDocument journal,
        IPlaybackSessionSource sessions,
        IQbittorrentClient qbit)
    {
        return new OperationalStatusService(
            sessions,
            new StaticJournalStore(journal),
            new ProcessInstanceIdentity(Guid.NewGuid()),
            new StaticPersistence(configuration),
            new StaticClientFactory(qbit),
            new StaticTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)));
    }

    private static PluginConfiguration Configuration(long revision)
    {
        return new PluginConfiguration
        {
            Revision = revision,
            QbittorrentBaseAddress = "http://qbit:8080",
            CredentialMode = QbittorrentCredentialMode.StoredApiKey,
            QbittorrentApiKey = "qbt_1234567890123456789012345678",
            ConnectionValidated = true,
            AlternativeLimitsEnabled = true,
            StopTorrentsEnabled = true,
            StopScope = TorrentScope.All,
            SelectedCategories = [],
            IncludeIncomplete = true,
            IncludeCompleted = true,
            MarkerTag = "jfStopped",
            ExclusionTags = ["jfNeverTouch", "manual"],
            ReleaseGraceSeconds = 60,
        };
    }

    private static ActivationJournalDocument Journal(long revision)
    {
        return new ActivationJournalDocument(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero),
            ImmutableArray.Create("historical-session"),
            new JournalConfigurationSnapshot(
                revision,
                true,
                true,
                TorrentScope.All,
                [],
                true,
                true,
                "jfStopped",
                ["jfNeverTouch", "manual"],
                TimeSpan.FromSeconds(60)),
            new QbittorrentEndpointIdentity("http", "qbit", 8080, "/"),
            new AlternativeLimitsJournalState(
                false,
                true,
                JournalMutationStage.Confirmed,
                JournalMutationStage.None),
            [],
            ProtectionPhase.Protecting,
            null,
            new DateTimeOffset(2026, 8, 14, 11, 59, 0, TimeSpan.Zero),
            null);
    }

    private static TorrentSnapshot Torrent(
        string hash,
        bool stopped,
        long amountLeft,
        params string[] tags)
    {
        return new TorrentSnapshot(hash, null, amountLeft, stopped, tags);
    }

    private sealed class StaticSessionSource(IReadOnlyList<PlaybackSessionSnapshot> sessions)
        : IPlaybackSessionSource
    {
        public Task<IReadOnlyList<PlaybackSessionSnapshot>> ReadAsync(
            CancellationToken cancellationToken) => Task.FromResult(sessions);
    }

    private sealed class StaticPersistence(PluginConfiguration configuration)
        : IPluginConfigurationPersistence
    {
        public PluginConfiguration Current => configuration;

        public void Save(PluginConfiguration next) => throw new NotSupportedException();
    }

    private sealed class StaticJournalStore(ActivationJournalDocument journal)
        : IActivationJournalStore
    {
        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ActivationJournalLoadResult(
                ActivationJournalLoadStatus.Interrupted,
                ActivationJournalAuthority.ProtectOnly,
                journal));
        }

        public ValueTask WriteAsync(
            ActivationJournalDocument document,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DeleteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyJournalStore : IActivationJournalStore
    {
        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ActivationJournalLoadResult(
                ActivationJournalLoadStatus.Missing,
                ActivationJournalAuthority.None,
                null));
        }

        public ValueTask WriteAsync(
            ActivationJournalDocument document,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DeleteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StaticClientFactory(IQbittorrentClient client)
        : IQbittorrentClientFactory
    {
        public IQbittorrentClient Create(QbittorrentEndpointIdentity endpoint) => client;

        public IQbittorrentClient Create(PluginConfiguration configuration) => client;
    }

    private sealed class StatusQbittorrentClient : IQbittorrentClient
    {
        public IReadOnlyList<TorrentSnapshot> Torrents { get; set; } = [];

        public bool AlternativeLimitsEnabled { get; set; }

        public QbittorrentClientException? Failure { get; set; }

        public Task<QbittorrentServerInfo> GetServerInfoAsync(CancellationToken cancellationToken)
        {
            ThrowIfFailed();
            return Task.FromResult(new QbittorrentServerInfo(
                new Version(5, 2, 3),
                new Version(2, 15, 1)));
        }

        public Task<IReadOnlyList<TorrentSnapshot>> GetTorrentsAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfFailed();
            return Task.FromResult(Torrents);
        }

        public Task<bool> GetAlternativeLimitsEnabledAsync(CancellationToken cancellationToken)
        {
            ThrowIfFailed();
            return Task.FromResult(AlternativeLimitsEnabled);
        }

        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddTagAsync(IEnumerable<string> hashes, string tag, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StartTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetAlternativeLimitsEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private void ThrowIfFailed()
        {
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
