using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.QBittorrentContractProbe;

internal static class Program
{
    private const string ContractTag = "qcontrolContract";

    public static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 2 || !Uri.TryCreate(arguments[0], UriKind.Absolute, out var baseAddress))
        {
            await Console.Error
                .WriteLineAsync("Usage: QbittorrentContractProbe <base-url> <secret-file>")
                .ConfigureAwait(false);
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var httpClient = new HttpClient();
        var client = new QbittorrentClient(
            httpClient,
            new QbittorrentConnectionOptions(baseAddress, TimeSpan.FromSeconds(10)),
            new SecretFileCredentialSource(arguments[1]));

        var server = await client.GetServerInfoAsync(timeout.Token).ConfigureAwait(false);
        Require(server.ApplicationVersion >= new Version(5, 2, 0), "Unexpected application version.");
        Require(server.WebApiVersion.Major == 2, "Unexpected Web API major version.");

        var categories = await client.GetCategoriesAsync(timeout.Token).ConfigureAwait(false);
        Require(categories.Contains("radarr", StringComparer.Ordinal), "Missing radarr category.");
        Require(categories.Contains("sonarr", StringComparer.Ordinal), "Missing sonarr category.");

        var torrents = await client.GetTorrentsAsync(timeout.Token).ConfigureAwait(false);
        Require(torrents.Count == 6, "Unexpected fixture torrent count.");
        var target = torrents.FirstOrDefault(torrent => torrent.IsCompleted && torrent.IsStopped)
            ?? throw new InvalidOperationException("No completed stopped fixture torrent exists.");
        var initialAlternativeLimits = await client
            .GetAlternativeLimitsEnabledAsync(timeout.Token)
            .ConfigureAwait(false);

        try
        {
            await client.SetAlternativeLimitsEnabledAsync(true, timeout.Token).ConfigureAwait(false);
            Require(
                await client.GetAlternativeLimitsEnabledAsync(timeout.Token).ConfigureAwait(false),
                "Alternative Limits did not enable.");

            await client.AddTagAsync([target.Hash], ContractTag, timeout.Token).ConfigureAwait(false);
            _ = await WaitForTorrentAsync(
                client,
                target.Hash,
                torrent => torrent.Tags.Contains(ContractTag),
                timeout.Token).ConfigureAwait(false);

            await client.StartTorrentsAsync([target.Hash], timeout.Token).ConfigureAwait(false);
            _ = await WaitForTorrentAsync(
                client,
                target.Hash,
                torrent => !torrent.IsStopped,
                timeout.Token).ConfigureAwait(false);

            await client.StopTorrentsAsync([target.Hash], timeout.Token).ConfigureAwait(false);
            _ = await WaitForTorrentAsync(
                client,
                target.Hash,
                torrent => torrent.IsStopped,
                timeout.Token).ConfigureAwait(false);

            await client.RemoveTagAsync([target.Hash], ContractTag, timeout.Token).ConfigureAwait(false);
            _ = await WaitForTorrentAsync(
                client,
                target.Hash,
                torrent => !torrent.Tags.Contains(ContractTag),
                timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            await client.StopTorrentsAsync([target.Hash], CancellationToken.None).ConfigureAwait(false);
            await client.RemoveTagAsync([target.Hash], ContractTag, CancellationToken.None).ConfigureAwait(false);
            await client
                .SetAlternativeLimitsEnabledAsync(initialAlternativeLimits, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<TorrentSnapshot> WaitForTorrentAsync(
        QbittorrentClient client,
        string hash,
        Func<TorrentSnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var torrents = await client.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
            var torrent = torrents.Single(candidate => string.Equals(
                candidate.Hash,
                hash,
                StringComparison.Ordinal));
            if (predicate(torrent))
            {
                return torrent;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("qBittorrent did not reach the expected contract state.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
