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

public sealed class StopTorrentsActionServiceTests
{
    [Fact]
    public async Task ProtectionPersistsIntentBeforeTagAndStopMutations()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("aaaaaaaa", "radarr", 1, false, []));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var result = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);

        Assert.Equal(
            [
                "qbit:list",
                "journal:marker-intent",
                "qbit:add-tag:aaaaaaaa:jfStopped",
                "qbit:list",
                "journal:marker-confirmed",
                "journal:stop-intent",
                "qbit:stop:aaaaaaaa",
                "qbit:list",
                "journal:stop-confirmed",
            ],
            events);
        var entry = Assert.Single(result.Torrents);
        Assert.Equal(JournalMutationStage.Confirmed, entry.MarkerAddStage);
        Assert.Equal(JournalMutationStage.Confirmed, entry.StopStage);
    }

    [Fact]
    public async Task RestorationConfirmsStartReadbackBeforeRemovingMarker()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("aaaaaaaa", "radarr", 1, true, ["jfStopped"]));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var result = await service.ReconcileRestorationAsync(
            CreateDocument(),
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.Equal(
            [
                "qbit:list",
                "journal:start-intent",
                "qbit:start:aaaaaaaa",
                "qbit:list",
                "journal:marker-remove-intent",
                "qbit:remove-tag:aaaaaaaa:jfStopped",
                "qbit:list",
                "journal:marker-remove-confirmed",
            ],
            events);
        var entry = Assert.Single(result.Torrents);
        Assert.Equal(JournalMutationStage.Confirmed, entry.StartStage);
        Assert.Equal(JournalMutationStage.Confirmed, entry.MarkerRemoveStage);
    }

    [Fact]
    public async Task RepeatedProtectionAtFixedPointDoesNotWriteOrMutate()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("aaaaaaaa", "radarr", 1, false, []));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);
        var protectedDocument = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);
        events.Clear();

        var repeated = await service.ReconcileProtectionAsync(
            protectedDocument,
            CancellationToken.None);

        Assert.Same(protectedDocument, repeated);
        Assert.DoesNotContain(events, item => item.StartsWith("journal:", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.StartsWith("qbit:add", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.StartsWith("qbit:stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProtectionBatchesEveryEligibleHashWithoutCyclingStoppedOrExcludedTorrents()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", "radarr", 1, false, []),
            new TorrentSnapshot("b", "radarr", 0, false, []),
            new TorrentSnapshot("c", "radarr", 1, false, []),
            new TorrentSnapshot("d", "radarr", 0, false, []),
            new TorrentSnapshot("e", "radarr", 1, false, []),
            new TorrentSnapshot("excluded-category", "sonarr", 1, false, []),
            new TorrentSnapshot("excluded-case", "Radarr", 1, false, []),
            new TorrentSnapshot("excluded-stopped", "radarr", 1, true, []),
            new TorrentSnapshot("excluded-tag", "radarr", 1, false, ["jfNeverTouch"]));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);
        var document = CreateDocument() with
        {
            Configuration = CreateDocument().Configuration with
            {
                StopScope = TorrentScope.SelectedCategories,
                SelectedCategories = ImmutableArray.Create("radarr"),
            },
        };

        await service.ReconcileProtectionAsync(document, CancellationToken.None);

        Assert.Contains("qbit:add-tag:a|b|c|d|e:jfStopped", events);
        Assert.Contains("qbit:stop:a|b|c|d|e", events);
        Assert.DoesNotContain(events, item => item.Contains("all", StringComparison.Ordinal));
        Assert.True(qbit.IsStopped("excluded-stopped"));
        Assert.False(qbit.HasTag("excluded-stopped", "jfStopped"));
        Assert.False(qbit.IsStopped("excluded-category"));
        Assert.False(qbit.IsStopped("excluded-case"));
        Assert.False(qbit.IsStopped("excluded-tag"));
    }

    [Fact]
    public async Task LaterPassAcquiresNewTorrentAndRestopsMarkedRestart()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, []));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);
        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);

        qbit.AddTorrent(new TorrentSnapshot("b", "sonarr", 1, false, []));
        events.Clear();
        document = await service.ReconcileProtectionAsync(document, CancellationToken.None);
        Assert.Contains("qbit:add-tag:b:jfStopped", events);
        Assert.Contains("qbit:stop:b", events);

        qbit.SetStopped("a", false);
        events.Clear();
        await service.ReconcileProtectionAsync(document, CancellationToken.None);
        Assert.DoesNotContain(events, item => item.StartsWith("qbit:add-tag:a", StringComparison.Ordinal));
        Assert.Contains("qbit:stop:a", events);
    }

    [Fact]
    public async Task PartialTagBatchStopsOnlyReadbackConfirmedHashThenConverges()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, []),
            new TorrentSnapshot("b", null, 1, false, []))
        {
            AddTagApplyCount = 1,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);

        Assert.Contains("qbit:add-tag:a|b:jfStopped", events);
        Assert.Contains("qbit:stop:a", events);
        Assert.DoesNotContain("qbit:stop:a|b", events);
        Assert.True(qbit.IsStopped("a"));
        Assert.False(qbit.IsStopped("b"));

        qbit.AddTagApplyCount = int.MaxValue;
        events.Clear();
        await service.ReconcileProtectionAsync(document, CancellationToken.None);
        Assert.Contains("qbit:add-tag:b:jfStopped", events);
        Assert.Contains("qbit:stop:b", events);
        Assert.True(qbit.IsStopped("b"));
    }

    [Fact]
    public async Task PartialStopBatchRetainsIntentForRunningHashThenConverges()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, []),
            new TorrentSnapshot("b", null, 1, false, []))
        {
            StopApplyCount = 1,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var document = await service.ReconcileProtectionAsync(
            CreateDocument(),
            CancellationToken.None);

        Assert.True(qbit.IsStopped("a"));
        Assert.False(qbit.IsStopped("b"));
        Assert.Equal(
            JournalMutationStage.IntentPersisted,
            document.Torrents.Single(entry => entry.Hash == "b").StopStage);

        qbit.StopApplyCount = int.MaxValue;
        events.Clear();
        var protectedDocument = await service.ReconcileProtectionAsync(
            document,
            CancellationToken.None);
        Assert.Contains("qbit:stop:b", events);
        Assert.True(qbit.IsStopped("b"));
        Assert.All(protectedDocument.Torrents, entry =>
            Assert.Equal(JournalMutationStage.Confirmed, entry.StopStage));
    }

    [Fact]
    public async Task FailedTagMutationLeavesDurableIntentAndRetryUsesReadback()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, []),
            new TorrentSnapshot("b", null, 1, false, []))
        {
            AddTagApplyCount = 1,
            ThrowAfterAddTag = true,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileProtectionAsync(CreateDocument(), CancellationToken.None));

        Assert.DoesNotContain(events, item => item.StartsWith("qbit:stop", StringComparison.Ordinal));
        var durable = Assert.Single(journal.Writes);
        Assert.All(
            durable.Torrents,
            entry => Assert.Equal(JournalMutationStage.IntentPersisted, entry.MarkerAddStage));

        qbit.ThrowAfterAddTag = false;
        qbit.AddTagApplyCount = int.MaxValue;
        events.Clear();
        var recovered = await service.ReconcileProtectionAsync(durable, CancellationToken.None);
        Assert.Contains("qbit:add-tag:b:jfStopped", events);
        Assert.Contains("qbit:stop:a|b", events);
        Assert.All(recovered.Torrents, entry =>
        {
            Assert.Equal(JournalMutationStage.Confirmed, entry.MarkerAddStage);
            Assert.Equal(JournalMutationStage.Confirmed, entry.StopStage);
        });
    }

    [Fact]
    public async Task RetryConfirmsStopThatSucceededBeforeReadbackFailed()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, []))
        {
            ThrowOnListCall = 3,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileProtectionAsync(CreateDocument(), CancellationToken.None));
        var durable = journal.Writes[^1];
        Assert.Equal(
            JournalMutationStage.IntentPersisted,
            Assert.Single(durable.Torrents).StopStage);
        Assert.True(qbit.IsStopped("a"));

        qbit.ThrowOnListCall = null;
        events.Clear();
        var recovered = await service.ReconcileProtectionAsync(durable, CancellationToken.None);

        Assert.Equal(JournalMutationStage.Confirmed, Assert.Single(recovered.Torrents).StopStage);
        Assert.DoesNotContain(events, item => item.StartsWith("qbit:stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestorationStartsOnlyMarkedNonExcludedTorrents()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("marked", null, 1, true, ["jfStopped"]),
            new TorrentSnapshot("unmarked", null, 1, true, []),
            new TorrentSnapshot("never", null, 1, true, ["jfStopped", "jfNeverTouch"]));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        await service.ReconcileRestorationAsync(
            CreateDocument(),
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.Contains("qbit:start:marked", events);
        Assert.Contains("qbit:remove-tag:marked:jfStopped", events);
        Assert.True(qbit.IsStopped("unmarked"));
        Assert.True(qbit.IsStopped("never"));
        Assert.True(qbit.HasTag("never", "jfStopped"));
    }

    [Fact]
    public async Task PartialStartRetainsUnresolvedMarkerThenConverges()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, true, ["jfStopped"]),
            new TorrentSnapshot("b", null, 1, true, ["jfStopped"]))
        {
            StartApplyCount = 1,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var document = await service.ReconcileRestorationAsync(
            CreateDocument(),
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.Contains("qbit:start:a|b", events);
        Assert.Contains("qbit:remove-tag:a:jfStopped", events);
        Assert.False(qbit.HasTag("a", "jfStopped"));
        Assert.True(qbit.HasTag("b", "jfStopped"));

        qbit.StartApplyCount = int.MaxValue;
        events.Clear();
        var restored = await service.ReconcileRestorationAsync(
            document,
            ActivationJournalAuthority.Full,
            CancellationToken.None);
        Assert.Contains("qbit:start:b", events);
        Assert.Contains("qbit:remove-tag:b:jfStopped", events);
        Assert.False(qbit.HasTag("b", "jfStopped"));
        Assert.All(restored.Torrents, entry =>
            Assert.Equal(JournalMutationStage.Confirmed, entry.MarkerRemoveStage));
    }

    [Fact]
    public async Task PartialMarkerRemovalRetainsIntentThenConverges()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, ["jfStopped"]),
            new TorrentSnapshot("b", null, 1, false, ["jfStopped"]))
        {
            RemoveTagApplyCount = 1,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var document = await service.ReconcileRestorationAsync(
            CreateDocument(),
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.False(qbit.HasTag("a", "jfStopped"));
        Assert.True(qbit.HasTag("b", "jfStopped"));
        Assert.Equal(
            JournalMutationStage.IntentPersisted,
            document.Torrents.Single(entry => entry.Hash == "b").MarkerRemoveStage);

        qbit.RemoveTagApplyCount = int.MaxValue;
        events.Clear();
        var restored = await service.ReconcileRestorationAsync(
            document,
            ActivationJournalAuthority.Full,
            CancellationToken.None);
        Assert.Contains("qbit:remove-tag:b:jfStopped", events);
        Assert.False(qbit.HasTag("b", "jfStopped"));
        Assert.All(restored.Torrents, entry =>
            Assert.Equal(JournalMutationStage.Confirmed, entry.MarkerRemoveStage));
    }

    [Fact]
    public async Task InterruptedJournalAuthorityCannotRestoreAnything()
    {
        var events = new List<string>();
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, true, ["jfStopped"]));
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileRestorationAsync(
                CreateDocument(),
                ActivationJournalAuthority.ProtectOnly,
                CancellationToken.None));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ConcurrentReconciliationIsSerializedAtTheApplicationSeam()
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var qbit = new RecordingQbittorrentClient(
            events,
            new TorrentSnapshot("a", null, 1, false, []))
        {
            FirstListEntered = entered,
            FirstListRelease = release.Task,
        };
        var journal = new RecordingJournalStore(events);
        using var service = new StopTorrentsActionService(qbit, journal);

        var first = service.ReconcileProtectionAsync(CreateDocument(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = service.ReconcileProtectionAsync(CreateDocument(), CancellationToken.None);

        Assert.Equal(1, qbit.ListCallCount);
        release.SetResult(true);
        await Task.WhenAll(first, second);
    }

    private static ActivationJournalDocument CreateDocument()
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: new Guid("7724001a-6348-4d61-9cb9-fd73bf42a713"),
            ActivationId: new Guid("1fea2d82-90b2-48e9-ac89-bcb436189ce3"),
            StartedAt: new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            SessionIds: ImmutableArray.Create("session-a"),
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
            if (document.Torrents.Length != 1)
            {
                events.Add("journal:write");
                return ValueTask.CompletedTask;
            }

            var entry = document.Torrents[0];
            var stage = entry.StopStage switch
            {
                JournalMutationStage.IntentPersisted => "stop-intent",
                JournalMutationStage.Confirmed => "stop-confirmed",
                _ when entry.MarkerRemoveStage == JournalMutationStage.Confirmed =>
                    "marker-remove-confirmed",
                _ when entry.MarkerRemoveStage == JournalMutationStage.IntentPersisted =>
                    "marker-remove-intent",
                _ when entry.StartStage == JournalMutationStage.IntentPersisted => "start-intent",
                _ when entry.MarkerAddStage == JournalMutationStage.IntentPersisted => "marker-intent",
                _ when entry.MarkerAddStage == JournalMutationStage.Confirmed => "marker-confirmed",
                _ => throw new Xunit.Sdk.XunitException("Unexpected journal state."),
            };
            events.Add($"journal:{stage}");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DeleteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingQbittorrentClient : IQbittorrentClient
    {
        private readonly List<string> _events;
        private readonly Dictionary<string, MutableTorrent> _torrents;

        public RecordingQbittorrentClient(
            List<string> events,
            params TorrentSnapshot[] torrents)
        {
            _events = events;
            _torrents = torrents.ToDictionary(
                torrent => torrent.Hash,
                torrent => new MutableTorrent(torrent),
                StringComparer.Ordinal);
        }

        public int AddTagApplyCount { get; set; } = int.MaxValue;

        public int StartApplyCount { get; set; } = int.MaxValue;

        public int StopApplyCount { get; set; } = int.MaxValue;

        public int RemoveTagApplyCount { get; set; } = int.MaxValue;

        public bool ThrowAfterAddTag { get; set; }

        public int? ThrowOnListCall { get; set; }

        public TaskCompletionSource<bool>? FirstListEntered { get; set; }

        public Task? FirstListRelease { get; set; }

        public int ListCallCount { get; private set; }

        public void AddTorrent(TorrentSnapshot torrent)
        {
            _torrents.Add(torrent.Hash, new MutableTorrent(torrent));
        }

        public void SetStopped(string hash, bool stopped)
        {
            _torrents[hash].IsStopped = stopped;
        }

        public bool IsStopped(string hash) => _torrents[hash].IsStopped;

        public bool HasTag(string hash, string tag) => _torrents[hash].Tags.Contains(tag);

        public async Task<IReadOnlyList<TorrentSnapshot>> GetTorrentsAsync(
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            _events.Add("qbit:list");
            if (ListCallCount == ThrowOnListCall)
            {
                throw new InvalidOperationException("Injected qBittorrent readback failure.");
            }

            if (ListCallCount == 1 && FirstListRelease is not null)
            {
                FirstListEntered?.SetResult(true);
                await FirstListRelease.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<TorrentSnapshot> snapshots = _torrents.Values
                .Select(torrent => torrent.Snapshot())
                .OrderBy(torrent => torrent.Hash, StringComparer.Ordinal)
                .ToArray();
            return snapshots;
        }

        public Task AddTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken)
        {
            var selected = hashes.Order(StringComparer.Ordinal).ToArray();
            _events.Add($"qbit:add-tag:{string.Join('|', selected)}:{tag}");
            foreach (var hash in selected.Take(AddTagApplyCount))
            {
                _torrents[hash].Tags.Add(tag);
            }

            if (ThrowAfterAddTag)
            {
                throw new InvalidOperationException("Injected qBittorrent boundary failure.");
            }

            return Task.CompletedTask;
        }

        public Task StopTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken)
        {
            var selected = hashes.Order(StringComparer.Ordinal).ToArray();
            _events.Add($"qbit:stop:{string.Join('|', selected)}");
            foreach (var hash in selected.Take(StopApplyCount))
            {
                _torrents[hash].IsStopped = true;
            }

            return Task.CompletedTask;
        }

        public Task<QbittorrentServerInfo> GetServerInfoAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> GetAlternativeLimitsEnabledAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StartTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken)
        {
            var selected = hashes.Order(StringComparer.Ordinal).ToArray();
            _events.Add($"qbit:start:{string.Join('|', selected)}");
            foreach (var hash in selected.Take(StartApplyCount))
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
            var selected = hashes.Order(StringComparer.Ordinal).ToArray();
            _events.Add($"qbit:remove-tag:{string.Join('|', selected)}:{tag}");
            foreach (var hash in selected.Take(RemoveTagApplyCount))
            {
                _torrents[hash].Tags.Remove(tag);
            }

            return Task.CompletedTask;
        }

        public Task SetAlternativeLimitsEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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
