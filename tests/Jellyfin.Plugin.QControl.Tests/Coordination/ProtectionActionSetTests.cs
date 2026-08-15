using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Coordination;

public sealed class ProtectionActionSetTests
{
    [Fact]
    public async Task FirstActionFailureReloadsIntentAndStillRunsSecondAction()
    {
        var initial = CreateDocument();
        var durableIntent = initial with
        {
            AlternativeLimits = initial.AlternativeLimits with
            {
                InitialEnabled = false,
                EnableStage = JournalMutationStage.IntentPersisted,
            },
        };
        var alternativeLimits = new Mock<IAlternativeLimitsActionService>();
        alternativeLimits
            .Setup(action => action.ReconcileProtectionAsync(initial, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QbittorrentClientException(
                QbittorrentClientError.Connection,
                "The qBittorrent endpoint is unavailable."));
        var stopTorrents = new Mock<IStopTorrentsActionService>();
        stopTorrents
            .Setup(action => action.ReconcileProtectionAsync(
                durableIntent,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(durableIntent);
        var store = new ReloadingJournalStore(durableIntent);
        var actions = new ProtectionActionSet(
            alternativeLimits.Object,
            stopTorrents.Object,
            store);

        var result = await actions.ReconcileProtectionAsync(
            initial,
            CancellationToken.None);

        Assert.Same(durableIntent, result.Journal);
        Assert.Equal(JournalFailureCode.Connection, result.Failure);
        stopTorrents.VerifyAll();
    }

    [Fact]
    public async Task RestorationSettlesOnlyAfterOwnedAndPerHashIntentsConfirm()
    {
        var document = CreateDocument() with
        {
            AlternativeLimits = new AlternativeLimitsJournalState(
                InitialEnabled: false,
                EnabledByActivation: true,
                EnableStage: JournalMutationStage.Confirmed,
                DisableStage: JournalMutationStage.Confirmed),
            Torrents = ImmutableArray.Create(new TorrentMutationJournalEntry(
                "aaaaaaaa",
                MarkerAddStage: JournalMutationStage.Confirmed,
                StopStage: JournalMutationStage.Confirmed,
                StartStage: JournalMutationStage.Confirmed,
                MarkerRemoveStage: JournalMutationStage.Confirmed)),
            Phase = ProtectionPhase.Restoring,
        };
        var alternativeLimits = new Mock<IAlternativeLimitsActionService>();
        alternativeLimits
            .Setup(action => action.ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        var stopTorrents = new Mock<IStopTorrentsActionService>();
        stopTorrents
            .Setup(action => action.ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        var actions = new ProtectionActionSet(
            alternativeLimits.Object,
            stopTorrents.Object,
            new ReloadingJournalStore(document));

        var settled = await actions.ReconcileRestorationAsync(
            document,
            ActivationJournalAuthority.Full,
            CancellationToken.None);
        var unresolvedDocument = document with
        {
            Torrents = ImmutableArray.Create(document.Torrents[0] with
            {
                MarkerRemoveStage = JournalMutationStage.IntentPersisted,
            }),
        };
        stopTorrents
            .Setup(action => action.ReconcileRestorationAsync(
                document,
                ActivationJournalAuthority.Full,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(unresolvedDocument);
        var unresolved = await actions.ReconcileRestorationAsync(
            document,
            ActivationJournalAuthority.Full,
            CancellationToken.None);

        Assert.True(settled.RestorationSettled);
        Assert.False(unresolved.RestorationSettled);
    }

    private static ActivationJournalDocument CreateDocument()
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: new Guid("cd34669c-b074-455c-98b7-b7b82ef20b46"),
            ActivationId: new Guid("e1980db5-35b6-4f86-af1c-bff6e91bd00c"),
            StartedAt: new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            SessionIds: ImmutableArray.Create("session-a"),
            Configuration: new JournalConfigurationSnapshot(
                Revision: 1,
                AlternativeLimitsEnabled: true,
                StopTorrentsEnabled: true,
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

    private sealed class ReloadingJournalStore(ActivationJournalDocument document)
        : IActivationJournalStore
    {
        public ValueTask WriteAsync(
            ActivationJournalDocument next,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ActivationJournalLoadResult> LoadAsync(
            Guid currentProcessInstanceId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ActivationJournalLoadResult(
                ActivationJournalLoadStatus.Active,
                ActivationJournalAuthority.Full,
                document));
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
