using System;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.AlternativeLimitsContractProbe;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 2 || !Uri.TryCreate(arguments[0], UriKind.Absolute, out var baseAddress))
        {
            await Console.Error
                .WriteLineAsync("Usage: AlternativeLimitsContractProbe <base-url> <secret-file>")
                .ConfigureAwait(false);
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var httpClient = new HttpClient();
        var client = new QbittorrentClient(
            httpClient,
            new QbittorrentConnectionOptions(baseAddress, TimeSpan.FromSeconds(10)),
            new SecretFileCredentialSource(arguments[1]));
        var journalPath = Path.Combine(
            Path.GetTempPath(),
            $"qcontrol-alternative-contract-{Guid.NewGuid():N}.json");
        var store = new ActivationJournalStore(
            journalPath,
            new PhysicalActivationJournalFileSystem());
        using var action = new AlternativeLimitsActionService(client, store);
        var initialMode = await client
            .GetAlternativeLimitsEnabledAsync(timeout.Token)
            .ConfigureAwait(false);

        try
        {
            await client.SetAlternativeLimitsEnabledAsync(false, timeout.Token)
                .ConfigureAwait(false);
            var owned = await action
                .ReconcileProtectionAsync(CreateDocument(baseAddress), timeout.Token)
                .ConfigureAwait(false);
            Require(
                await client.GetAlternativeLimitsEnabledAsync(timeout.Token).ConfigureAwait(false),
                "Protection did not enable Alternative Limits.");
            Require(
                owned.AlternativeLimits.InitialEnabled == false
                && owned.AlternativeLimits.EnabledByActivation
                && owned.AlternativeLimits.EnableStage == JournalMutationStage.Confirmed,
                "The disabled-to-enabled transition was not durably owned.");

            await client.SetAlternativeLimitsEnabledAsync(false, timeout.Token)
                .ConfigureAwait(false);
            owned = await action.ReconcileProtectionAsync(owned, timeout.Token).ConfigureAwait(false);
            Require(
                await client.GetAlternativeLimitsEnabledAsync(timeout.Token).ConfigureAwait(false),
                "Protection did not re-enable Alternative Limits.");
            Require(
                owned.AlternativeLimits.EnabledByActivation
                && owned.AlternativeLimits.EnableStage == JournalMutationStage.Confirmed,
                "Re-enforcement lost activation ownership.");

            owned = owned with { Phase = ProtectionPhase.Restoring };
            await store.WriteAsync(owned, timeout.Token).ConfigureAwait(false);
            owned = await action
                .ReconcileRestorationAsync(
                    owned,
                    ActivationJournalAuthority.Full,
                    timeout.Token)
                .ConfigureAwait(false);
            Require(
                !await client.GetAlternativeLimitsEnabledAsync(timeout.Token).ConfigureAwait(false),
                "Owned restoration did not disable Alternative Limits.");
            Require(
                owned.AlternativeLimits.DisableStage == JournalMutationStage.Confirmed,
                "Owned restoration was not durably confirmed.");

            await client.SetAlternativeLimitsEnabledAsync(true, timeout.Token)
                .ConfigureAwait(false);
            var unowned = await action
                .ReconcileProtectionAsync(CreateDocument(baseAddress), timeout.Token)
                .ConfigureAwait(false);
            Require(
                unowned.AlternativeLimits.InitialEnabled == true
                && !unowned.AlternativeLimits.EnabledByActivation,
                "An initially enabled mode was incorrectly owned.");
            unowned = unowned with { Phase = ProtectionPhase.Restoring };
            await store.WriteAsync(unowned, timeout.Token).ConfigureAwait(false);
            _ = await action
                .ReconcileRestorationAsync(
                    unowned,
                    ActivationJournalAuthority.Full,
                    timeout.Token)
                .ConfigureAwait(false);
            Require(
                await client.GetAlternativeLimitsEnabledAsync(timeout.Token).ConfigureAwait(false),
                "Unowned restoration changed the initially enabled mode.");
        }
        finally
        {
            await client.SetAlternativeLimitsEnabledAsync(initialMode, CancellationToken.None)
                .ConfigureAwait(false);
            await store.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return 0;
    }

    private static ActivationJournalDocument CreateDocument(Uri endpoint)
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: Guid.NewGuid(),
            ActivationId: Guid.NewGuid(),
            StartedAt: DateTimeOffset.UtcNow,
            SessionIds: ImmutableArray.Create("contract-session"),
            Configuration: new JournalConfigurationSnapshot(
                Revision: 1,
                AlternativeLimitsEnabled: true,
                StopTorrentsEnabled: false,
                StopScope: TorrentScope.All,
                SelectedCategories: [],
                IncludeIncomplete: true,
                IncludeCompleted: true,
                MarkerTag: "qcontrol-resume",
                ExclusionTags: ["qcontrol-ignore"],
                ReleaseGrace: TimeSpan.Zero),
            Endpoint: new QbittorrentEndpointIdentity(
                endpoint.Scheme,
                endpoint.Host,
                endpoint.Port,
                endpoint.AbsolutePath),
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
