using System;
using System.Collections.Immutable;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Tests.Journal;

internal static class JournalTestData
{
    public static ActivationJournalDocument Create(Guid processInstanceId)
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            processInstanceId,
            ActivationId: new Guid("835a3042-3351-4e52-8e8d-1bfd22703c46"),
            StartedAt: new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            SessionIds: ImmutableArray.Create("session-a", "session-b"),
            Configuration: new JournalConfigurationSnapshot(
                Revision: 7,
                AlternativeLimitsEnabled: true,
                StopTorrentsEnabled: true,
                StopScope: TorrentScope.SelectedCategories,
                SelectedCategories: ImmutableArray.Create("radarr", "sonarr"),
                IncludeIncomplete: true,
                IncludeCompleted: true,
                MarkerTag: "jfStopped",
                NeverTouchTag: "jfNeverTouch",
                ReleaseGrace: TimeSpan.FromSeconds(60)),
            Endpoint: new QbittorrentEndpointIdentity("http", "qbittorrent", 18180, "/control"),
            AlternativeLimits: new AlternativeLimitsJournalState(
                InitialEnabled: false,
                EnabledByActivation: true,
                EnableStage: JournalMutationStage.Confirmed,
                DisableStage: JournalMutationStage.None),
            Torrents: ImmutableArray.Create(
                new TorrentMutationJournalEntry(
                    "aaaaaaaa",
                    MarkerAddStage: JournalMutationStage.Confirmed,
                    StopStage: JournalMutationStage.IntentPersisted,
                    StartStage: JournalMutationStage.None,
                    MarkerRemoveStage: JournalMutationStage.None)),
            Phase: ProtectionPhase.ReleasePending,
            ReleaseDueAt: new DateTimeOffset(2026, 8, 14, 12, 1, 0, TimeSpan.Zero),
            LastSuccessfulReconciliation: new DateTimeOffset(2026, 8, 14, 12, 0, 30, TimeSpan.Zero),
            LastFailure: JournalFailureCode.Connection);
    }
}
