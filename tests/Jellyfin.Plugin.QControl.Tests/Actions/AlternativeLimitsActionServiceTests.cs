using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Actions;

public sealed class AlternativeLimitsActionServiceTests
{
    [Fact]
    public async Task InitiallyDisabledModeIsJournaledEnabledAndOwned()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);

        var result = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);

        Assert.Equal(
            [
                "qbit:get:false",
                "journal:enable-intent:unowned",
                "qbit:set:true",
                "qbit:get:true",
                "journal:enable-confirmed:owned",
            ],
            events);
        Assert.Equal(false, result.AlternativeLimits.InitialEnabled);
        Assert.True(result.AlternativeLimits.EnabledByActivation);
        Assert.Equal(JournalMutationStage.Confirmed, result.AlternativeLimits.EnableStage);
    }

    [Fact]
    public async Task InitiallyEnabledModeIsObservedWithoutMutationOrOwnership()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: true);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);

        var result = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);

        Assert.Equal(["qbit:get:true", "journal:observed-enabled:unowned"], events);
        Assert.Equal(true, result.AlternativeLimits.InitialEnabled);
        Assert.False(result.AlternativeLimits.EnabledByActivation);
        Assert.Equal(JournalMutationStage.None, result.AlternativeLimits.EnableStage);
    }

    [Fact]
    public async Task ManualDisableDuringOwnedProtectionIsJournaledAndReenabled()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        qbit.Enabled = false;
        events.Clear();

        var reasserted = await service.ReconcileProtectionAsync(
            document,
            CancellationToken.None);

        Assert.Equal(
            [
                "qbit:get:false",
                "journal:enable-intent:owned",
                "qbit:set:true",
                "qbit:get:true",
                "journal:enable-confirmed:owned",
            ],
            events);
        Assert.True(qbit.Enabled);
        Assert.True(reasserted.AlternativeLimits.EnabledByActivation);
    }

    [Fact]
    public async Task RepeatedProtectionAtFixedPointOnlyReadsMode()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        events.Clear();

        var repeated = await service.ReconcileProtectionAsync(
            document,
            CancellationToken.None);

        Assert.Same(document, repeated);
        Assert.Equal(["qbit:get:true"], events);
    }

    [Fact]
    public async Task OwnedTransitionIsJournaledAndDisabledOnNormalRestoration()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        document = document with { Phase = ProtectionPhase.Restoring };
        events.Clear();

        var restored = await service.ReconcileRestorationAsync(
            document,
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.Equal(
            [
                "qbit:get:true",
                "journal:disable-intent:owned",
                "qbit:set:false",
                "qbit:get:false",
                "journal:disable-confirmed:owned",
            ],
            events);
        Assert.False(qbit.Enabled);
        Assert.Equal(JournalMutationStage.Confirmed, restored.AlternativeLimits.DisableStage);
    }

    [Fact]
    public async Task InitiallyEnabledUnownedModeRemainsEnabledOnRestoration()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: true);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        events.Clear();

        var restored = await service.ReconcileRestorationAsync(
            document with { Phase = ProtectionPhase.Restoring },
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.Same(document.AlternativeLimits, restored.AlternativeLimits);
        Assert.Equal(["qbit:get:true"], events);
        Assert.True(qbit.Enabled);
    }

    [Fact]
    public async Task InitiallyEnabledModeIsReassertedWithoutTakingOwnership()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: true);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        qbit.Enabled = false;
        events.Clear();

        document = await service.ReconcileProtectionAsync(document, CancellationToken.None);

        Assert.Equal(
            [
                "qbit:get:false",
                "journal:enable-intent:unowned",
                "qbit:set:true",
                "qbit:get:true",
                "journal:enable-confirmed:unowned",
            ],
            events);
        Assert.True(qbit.Enabled);
        Assert.False(document.AlternativeLimits.EnabledByActivation);

        events.Clear();
        await service.ReconcileRestorationAsync(
            document with { Phase = ProtectionPhase.Restoring },
            ActivationJournalAuthority.Full,
            CancellationToken.None);
        Assert.Equal(["qbit:get:true"], events);
        Assert.True(qbit.Enabled);
    }

    [Fact]
    public async Task EnableSuccessBeforeFailureIsConfirmedFromRetryReadback()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false)
        {
            ThrowAfterSet = true,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileProtectionAsync(CreateDocument(), CancellationToken.None));
        var durable = Assert.Single(journal.Writes);
        Assert.Equal(JournalMutationStage.IntentPersisted, durable.AlternativeLimits.EnableStage);
        Assert.False(durable.AlternativeLimits.EnabledByActivation);
        Assert.True(qbit.Enabled);

        qbit.ThrowAfterSet = false;
        events.Clear();
        var recovered = await service.ReconcileProtectionAsync(
            durable,
            CancellationToken.None);
        Assert.Equal(
            ["qbit:get:true", "journal:enable-confirmed:owned"],
            events);
        Assert.True(recovered.AlternativeLimits.EnabledByActivation);
    }

    [Fact]
    public async Task DisableSuccessBeforeFailureIsConfirmedFromRetryReadback()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        qbit.ThrowAfterSet = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileRestorationAsync(
                document with { Phase = ProtectionPhase.Restoring },
                ActivationJournalAuthority.Full,
                CancellationToken.None));
        var durable = journal.Writes[^1];
        Assert.Equal(JournalMutationStage.IntentPersisted, durable.AlternativeLimits.DisableStage);
        Assert.False(qbit.Enabled);

        qbit.ThrowAfterSet = false;
        events.Clear();
        var recovered = await service.ReconcileRestorationAsync(
            durable,
            ActivationJournalAuthority.Full,
            CancellationToken.None);
        Assert.Equal(
            ["qbit:get:false", "journal:disable-confirmed:owned"],
            events);
        Assert.Equal(JournalMutationStage.Confirmed, recovered.AlternativeLimits.DisableStage);
    }

    [Fact]
    public async Task RestorationClaimsPendingSuccessfulEnableBeforeDisablingIt()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false)
        {
            ThrowAfterSet = true,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileProtectionAsync(CreateDocument(), CancellationToken.None));
        var durable = journal.Writes[^1] with { Phase = ProtectionPhase.Restoring };
        qbit.ThrowAfterSet = false;
        events.Clear();

        var restored = await service.ReconcileRestorationAsync(
            durable,
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.Equal(
            [
                "qbit:get:true",
                "journal:enable-confirmed:owned",
                "journal:disable-intent:owned",
                "qbit:set:false",
                "qbit:get:false",
                "journal:disable-confirmed:owned",
            ],
            events);
        Assert.False(qbit.Enabled);
        Assert.True(restored.AlternativeLimits.EnabledByActivation);
        Assert.Equal(JournalMutationStage.Confirmed, restored.AlternativeLimits.DisableStage);
    }

    [Fact]
    public async Task InterruptedJournalAuthorityCannotRestoreOwnedMode()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(events, enabled: false);
        var journal = new RecordingJournalStore(events);
        using var service = new AlternativeLimitsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        events.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileRestorationAsync(
                document with { Phase = ProtectionPhase.Restoring },
                ActivationJournalAuthority.ProtectOnly,
                CancellationToken.None));

        Assert.Empty(events);
        Assert.True(qbit.Enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopTorrentsAndAlternativeLimitsComposeInEitherOrder(
        bool alternativeLimitsFirst)
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(
            events,
            enabled: false,
            new TorrentSnapshot("a", "radarr", 1, false, []));
        var journal = new RecordingJournalStore(events);
        using var alternativeLimits = new AlternativeLimitsActionService(qbit, journal);
        using var stopTorrents = new StopTorrentsActionService(qbit, journal);
        var initial = CreateDocument();
        initial = initial with
        {
            Configuration = initial.Configuration with { StopTorrentsEnabled = true },
        };

        var protectedDocument = alternativeLimitsFirst
            ? await ProtectAlternativeThenTorrentsAsync(
                initial,
                alternativeLimits,
                stopTorrents)
            : await ProtectTorrentsThenAlternativeAsync(
                initial,
                alternativeLimits,
                stopTorrents);

        Assert.True(qbit.Enabled);
        Assert.True(qbit.IsStopped("a"));
        Assert.True(qbit.HasTag("a", "jfStopped"));
        Assert.True(protectedDocument.AlternativeLimits.EnabledByActivation);
        Assert.Equal(
            JournalMutationStage.Confirmed,
            Assert.Single(protectedDocument.Torrents).StopStage);

        protectedDocument = protectedDocument with { Phase = ProtectionPhase.Restoring };
        var restored = alternativeLimitsFirst
            ? await RestoreAlternativeThenTorrentsAsync(
                protectedDocument,
                alternativeLimits,
                stopTorrents)
            : await RestoreTorrentsThenAlternativeAsync(
                protectedDocument,
                alternativeLimits,
                stopTorrents);

        Assert.False(qbit.Enabled);
        Assert.False(qbit.IsStopped("a"));
        Assert.False(qbit.HasTag("a", "jfStopped"));
        Assert.Equal(JournalMutationStage.Confirmed, restored.AlternativeLimits.DisableStage);
        Assert.Equal(
            JournalMutationStage.Confirmed,
            Assert.Single(restored.Torrents).MarkerRemoveStage);
    }

    [Fact]
    public async Task AlternativeLimitsFailureDoesNotInventStateOrBlockStopTorrents()
    {
        var events = new List<string>();
        var qbit = new AlternativeLimitsQbittorrentClient(
            events,
            enabled: false,
            new TorrentSnapshot("a", null, 1, false, []))
        {
            ThrowBeforeSet = true,
        };
        var journal = new RecordingJournalStore(events);
        using var alternativeLimits = new AlternativeLimitsActionService(qbit, journal);
        using var stopTorrents = new StopTorrentsActionService(qbit, journal);
        var initial = CreateDocument();
        initial = initial with
        {
            Configuration = initial.Configuration with { StopTorrentsEnabled = true },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            alternativeLimits.ReconcileProtectionAsync(initial, CancellationToken.None));
        var durable = journal.Writes[^1];
        qbit.ThrowBeforeSet = false;
        var stopped = await stopTorrents.ReconcileProtectionAsync(
            durable,
            CancellationToken.None);

        Assert.False(qbit.Enabled);
        Assert.Equal(
            JournalMutationStage.IntentPersisted,
            stopped.AlternativeLimits.EnableStage);
        Assert.False(stopped.AlternativeLimits.EnabledByActivation);
        Assert.True(qbit.IsStopped("a"));
        Assert.Equal(JournalMutationStage.Confirmed, Assert.Single(stopped.Torrents).StopStage);
    }

    private static async Task<ActivationJournalDocument> ProtectAlternativeThenTorrentsAsync(
        ActivationJournalDocument document,
        AlternativeLimitsActionService alternativeLimits,
        StopTorrentsActionService stopTorrents)
    {
        document = await alternativeLimits
            .ReconcileProtectionAsync(document, CancellationToken.None)
            .ConfigureAwait(false);
        return await stopTorrents
            .ReconcileProtectionAsync(document, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<ActivationJournalDocument> ProtectTorrentsThenAlternativeAsync(
        ActivationJournalDocument document,
        AlternativeLimitsActionService alternativeLimits,
        StopTorrentsActionService stopTorrents)
    {
        document = await stopTorrents
            .ReconcileProtectionAsync(document, CancellationToken.None)
            .ConfigureAwait(false);
        return await alternativeLimits
            .ReconcileProtectionAsync(document, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<ActivationJournalDocument> RestoreAlternativeThenTorrentsAsync(
        ActivationJournalDocument document,
        AlternativeLimitsActionService alternativeLimits,
        StopTorrentsActionService stopTorrents)
    {
        document = await alternativeLimits
            .ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                CancellationToken.None)
            .ConfigureAwait(false);
        return await stopTorrents
            .ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<ActivationJournalDocument> RestoreTorrentsThenAlternativeAsync(
        ActivationJournalDocument document,
        AlternativeLimitsActionService alternativeLimits,
        StopTorrentsActionService stopTorrents)
    {
        document = await stopTorrents
            .ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                CancellationToken.None)
            .ConfigureAwait(false);
        return await alternativeLimits
            .ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static ActivationJournalDocument CreateDocument()
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: new Guid("c81fb7f1-00bb-47eb-81e6-6cf44fc27f20"),
            ActivationId: new Guid("ed8822a8-9522-4592-b253-91f42238693c"),
            StartedAt: new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            SessionIds: ImmutableArray.Create("session-a"),
            Configuration: new JournalConfigurationSnapshot(
                Revision: 1,
                AlternativeLimitsEnabled: true,
                StopTorrentsEnabled: false,
                StopScope: TorrentScope.All,
                SelectedCategories: [],
                IncludeIncomplete: true,
                IncludeCompleted: true,
                MarkerTag: "jfStopped",
                ExclusionTags: ["jfNeverTouch"],
                ReleaseGrace: TimeSpan.FromSeconds(60)),
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

    private sealed class RecordingJournalStore(List<string> events) : IActivationJournalStore
    {
        public List<ActivationJournalDocument> Writes { get; } = [];

        public ValueTask WriteAsync(
            ActivationJournalDocument document,
            CancellationToken cancellationToken)
        {
            Writes.Add(document);
            var state = document.AlternativeLimits;
            var observation = state.DisableStage switch
            {
                JournalMutationStage.IntentPersisted => "disable-intent",
                JournalMutationStage.Confirmed => "disable-confirmed",
                _ when state.InitialEnabled == true
                    && state.EnableStage == JournalMutationStage.None => "observed-enabled",
                _ => state.EnableStage switch
                {
                    JournalMutationStage.IntentPersisted => "enable-intent",
                    JournalMutationStage.Confirmed => "enable-confirmed",
                    _ => "observed-disabled",
                },
            };
            events.Add($"journal:{observation}:{(state.EnabledByActivation ? "owned" : "unowned")}");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DeleteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AlternativeLimitsQbittorrentClient : IQbittorrentClient
    {
        private readonly List<string> _events;
        private readonly Dictionary<string, MutableTorrent> _torrents;

        public AlternativeLimitsQbittorrentClient(
            List<string> events,
            bool enabled,
            params TorrentSnapshot[] torrents)
        {
            _events = events;
            Enabled = enabled;
            _torrents = torrents.ToDictionary(
                torrent => torrent.Hash,
                torrent => new MutableTorrent(torrent),
                StringComparer.Ordinal);
        }

        public bool Enabled { get; set; }

        public bool ThrowAfterSet { get; set; }

        public bool ThrowBeforeSet { get; set; }

        public bool IsStopped(string hash) => _torrents[hash].IsStopped;

        public bool HasTag(string hash, string tag) => _torrents[hash].Tags.Contains(tag);

        public Task<bool> GetAlternativeLimitsEnabledAsync(CancellationToken cancellationToken)
        {
            _events.Add($"qbit:get:{(Enabled ? "true" : "false")}");
            return Task.FromResult(Enabled);
        }

        public Task SetAlternativeLimitsEnabledAsync(
            bool requested,
            CancellationToken cancellationToken)
        {
            _events.Add($"qbit:set:{(requested ? "true" : "false")}");
            if (ThrowBeforeSet)
            {
                throw new InvalidOperationException("Injected Alternative Limits failure.");
            }

            Enabled = requested;
            if (ThrowAfterSet)
            {
                throw new InvalidOperationException("Injected Alternative Limits failure.");
            }

            return Task.CompletedTask;
        }

        public Task<QbittorrentServerInfo> GetServerInfoAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TorrentSnapshot>> GetTorrentsAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TorrentSnapshot> torrents = _torrents.Values
                .Select(torrent => torrent.Snapshot())
                .OrderBy(torrent => torrent.Hash, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(torrents);
        }

        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken)
        {
            foreach (var hash in hashes)
            {
                _torrents[hash].Tags.Add(tag);
            }

            return Task.CompletedTask;
        }

        public Task StopTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken)
        {
            foreach (var hash in hashes)
            {
                _torrents[hash].IsStopped = true;
            }

            return Task.CompletedTask;
        }

        public Task StartTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken)
        {
            foreach (var hash in hashes)
            {
                _torrents[hash].IsStopped = false;
            }

            return Task.CompletedTask;
        }

        public Task RemoveTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken)
        {
            foreach (var hash in hashes)
            {
                _torrents[hash].Tags.Remove(tag);
            }

            return Task.CompletedTask;
        }

        private sealed class MutableTorrent(TorrentSnapshot snapshot)
        {
            public string Hash { get; } = snapshot.Hash;

            public string? Category { get; } = snapshot.Category;

            public long RemainingBytes { get; } = snapshot.RemainingBytes;

            public bool IsStopped { get; set; } = snapshot.IsStopped;

            public HashSet<string> Tags { get; } = new(snapshot.Tags, StringComparer.Ordinal);

            public TorrentSnapshot Snapshot() =>
                new(Hash, Category, RemainingBytes, IsStopped, Tags);
        }
    }
}
