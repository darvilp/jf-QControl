using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.ActionContractProbe;

internal static class Program
{
    private const string MarkerTag = "qcontrolActionContract";
    private const string NeverTouchTag = "jfNeverTouch";

    public static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 2 || !Uri.TryCreate(arguments[0], UriKind.Absolute, out var baseAddress))
        {
            await Console.Error
                .WriteLineAsync("Usage: ActionContractProbe <base-url> <secret-file>")
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
            $"qcontrol-action-contract-{Guid.NewGuid():N}.json");
        var store = new ActivationJournalStore(
            journalPath,
            new PhysicalActivationJournalFileSystem());
        using var action = new StopTorrentsActionService(client, store);
        string[] initiallyRunningHashes = [];

        try
        {
            var initial = await client.GetTorrentsAsync(timeout.Token).ConfigureAwait(false);
            Require(initial.Count == 6, "Unexpected fixture torrent count.");
            var initialCategories = initial.ToDictionary(
                torrent => torrent.Hash,
                torrent => torrent.Category,
                StringComparer.Ordinal);
            initiallyRunningHashes = initial
                .Where(torrent => !torrent.IsStopped)
                .Where(torrent => !torrent.Tags.Contains(NeverTouchTag))
                .Select(torrent => torrent.Hash)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var initiallyStoppedHashes = initial
                .Where(torrent => torrent.IsStopped)
                .Select(torrent => torrent.Hash)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Require(initiallyRunningHashes.Length >= 3, "Fixture lacks running protection targets.");

            var processId = Guid.NewGuid();
            var document = CreateDocument(processId, baseAddress);
            document = await ReconcileUntilAsync(
                document,
                action.ReconcileProtectionAsync,
                client,
                torrents => initiallyRunningHashes.All(hash =>
                    Find(torrents, hash) is { IsStopped: true } torrent
                    && torrent.Tags.Contains(MarkerTag)),
                timeout.Token).ConfigureAwait(false);

            var protectedState = await client.GetTorrentsAsync(timeout.Token).ConfigureAwait(false);
            Require(
                initiallyStoppedHashes.All(hash => !Find(protectedState, hash).Tags.Contains(MarkerTag)),
                "An already-stopped fixture was incorrectly acquired.");
            Require(
                protectedState.All(torrent => string.Equals(
                    torrent.Category,
                    initialCategories[torrent.Hash],
                    StringComparison.Ordinal)),
                "Protection changed a fixture category.");

            document = document with { Phase = ProtectionPhase.Restoring };
            await store.WriteAsync(document, timeout.Token).ConfigureAwait(false);
            document = await ReconcileUntilAsync(
                document,
                (current, cancellationToken) => action.ReconcileRestorationAsync(
                    current,
                    ActivationJournalAuthority.Full,
                    cancellationToken),
                client,
                torrents => initiallyRunningHashes.All(hash =>
                    Find(torrents, hash) is { IsStopped: false } torrent
                    && !torrent.Tags.Contains(MarkerTag)),
                timeout.Token).ConfigureAwait(false);

            var restoredState = await client.GetTorrentsAsync(timeout.Token).ConfigureAwait(false);
            Require(
                initiallyStoppedHashes.All(hash => Find(restoredState, hash).IsStopped),
                "An unmarked stopped fixture was incorrectly started.");
            Require(
                restoredState
                    .Where(torrent => torrent.Tags.Contains(NeverTouchTag))
                    .All(torrent => torrent.IsStopped && !torrent.Tags.Contains(MarkerTag)),
                "The Never-touch fixture was mutated.");
            Require(
                restoredState.All(torrent => string.Equals(
                    torrent.Category,
                    initialCategories[torrent.Hash],
                    StringComparison.Ordinal)),
                "Restoration changed a fixture category.");

            await store.DeleteAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            if (initiallyRunningHashes.Length > 0)
            {
                await client
                    .StartTorrentsAsync(initiallyRunningHashes, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var current = await client.GetTorrentsAsync(CancellationToken.None).ConfigureAwait(false);
            var markedHashes = current
                .Where(torrent => torrent.Tags.Contains(MarkerTag))
                .Select(torrent => torrent.Hash)
                .ToArray();
            if (markedHashes.Length > 0)
            {
                await client
                    .RemoveTagAsync(markedHashes, MarkerTag, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await store.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return 0;
    }

    private static ActivationJournalDocument CreateDocument(Guid processId, Uri endpoint)
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: processId,
            ActivationId: Guid.NewGuid(),
            StartedAt: DateTimeOffset.UtcNow,
            SessionIds: ImmutableArray.Create("contract-session"),
            Configuration: new JournalConfigurationSnapshot(
                Revision: 1,
                AlternativeLimitsEnabled: false,
                StopTorrentsEnabled: true,
                StopScope: TorrentScope.All,
                SelectedCategories: [],
                IncludeIncomplete: true,
                IncludeCompleted: true,
                MarkerTag,
                NeverTouchTag,
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

    private static async Task<ActivationJournalDocument> ReconcileUntilAsync(
        ActivationJournalDocument document,
        Func<ActivationJournalDocument, CancellationToken, Task<ActivationJournalDocument>> reconcile,
        QbittorrentClient client,
        Func<IReadOnlyList<TorrentSnapshot>, bool> condition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            document = await reconcile(document, cancellationToken).ConfigureAwait(false);
            var torrents = await client.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
            if (condition(torrents))
            {
                return document;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The action did not reach its expected fixture state.");
    }

    private static TorrentSnapshot Find(
        IReadOnlyList<TorrentSnapshot> torrents,
        string hash)
    {
        return torrents.Single(torrent => string.Equals(torrent.Hash, hash, StringComparison.Ordinal));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
