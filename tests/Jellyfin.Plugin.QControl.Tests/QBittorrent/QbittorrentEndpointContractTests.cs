using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.QBittorrent;

public sealed class QbittorrentEndpointContractTests
{
    private const string ApiKey = "qbt_0123456789abcdefghijklmnopqr";

    [Fact]
    public async Task TorrentReadMapsOnlyNeutralPolicyFields()
    {
        const string payload = """
            [
              {
                "hash": "bbbb",
                "name": "must-not-cross-the-seam",
                "category": "",
                "amount_left": 0,
                "state": "stoppedUP",
                "tags": "jfStopped, jfNeverTouch"
              },
              {
                "hash": "aaaa",
                "name": "also-private",
                "category": "sonarr",
                "amount_left": 42,
                "state": "queuedDL",
                "tags": "fixture"
              }
            ]
            """;
        using var handler = new EndpointHandler(path => path.EndsWith(
            "/torrents/info",
            StringComparison.Ordinal)
            ? payload
            : throw new InvalidOperationException("Unexpected endpoint."));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var torrents = await client
            .GetTorrentsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Collection(
            torrents,
            first =>
            {
                Assert.Equal("aaaa", first.Hash);
                Assert.Equal("sonarr", first.Category);
                Assert.Equal(42, first.RemainingBytes);
                Assert.False(first.IsStopped);
                Assert.Equal(["fixture"], first.Tags);
            },
            second =>
            {
                Assert.Equal("bbbb", second.Hash);
                Assert.Null(second.Category);
                Assert.True(second.IsCompleted);
                Assert.True(second.IsStopped);
                Assert.Equal(["jfNeverTouch", "jfStopped"], second.Tags.Order(StringComparer.Ordinal));
            });
    }

    [Fact]
    public async Task CategoryTagAndAlternativeLimitsReadsAreTypedAndDeterministic()
    {
        using var handler = new EndpointHandler(path => path switch
        {
            "/api/v2/torrents/categories" => "{\"sonarr\":{},\"TV Épisodes\":{},\"radarr\":{}}",
            "/api/v2/torrents/tags" => "[\"manual\",\"Cross Seed\",\"manual\"]",
            "/api/v2/transfer/speedLimitsMode" => "1",
            _ => throw new InvalidOperationException("Unexpected endpoint."),
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var categories = await client
            .GetCategoriesAsync(CancellationToken.None)
            .ConfigureAwait(true);
        var tags = await client
            .GetTagsAsync(CancellationToken.None)
            .ConfigureAwait(true);
        var alternativeLimits = await client
            .GetAlternativeLimitsEnabledAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(["TV Épisodes", "radarr", "sonarr"], categories);
        Assert.Equal(["Cross Seed", "manual"], tags);
        Assert.True(alternativeLimits);
    }

    [Fact]
    public async Task MutationsUseOnlyAllowlistedPostsAndDeterministicFormEncoding()
    {
        using var handler = new EndpointHandler(_ => string.Empty);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.AddTagAsync(["bbbb", "aaaa", "aaaa"], "jf Støpped", CancellationToken.None)
            .ConfigureAwait(true);
        await client.StopTorrentsAsync(["bbbb", "aaaa"], CancellationToken.None)
            .ConfigureAwait(true);
        await client.StartTorrentsAsync(["aaaa"], CancellationToken.None)
            .ConfigureAwait(true);
        await client.RemoveTagAsync(["aaaa"], "jf Støpped", CancellationToken.None)
            .ConfigureAwait(true);
        await client.SetAlternativeLimitsEnabledAsync(true, CancellationToken.None)
            .ConfigureAwait(true);
        await client.SetAlternativeLimitsEnabledAsync(false, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(
            [
                new RecordedRequest("/api/v2/torrents/addTags", "hashes=aaaa%7Cbbbb&tags=jf+St%C3%B8pped"),
                new RecordedRequest("/api/v2/torrents/stop", "hashes=aaaa%7Cbbbb"),
                new RecordedRequest("/api/v2/torrents/start", "hashes=aaaa"),
                new RecordedRequest("/api/v2/torrents/removeTags", "hashes=aaaa&tags=jf+St%C3%B8pped"),
                new RecordedRequest("/api/v2/transfer/setSpeedLimitsMode", "mode=1"),
                new RecordedRequest("/api/v2/transfer/setSpeedLimitsMode", "mode=0"),
            ],
            handler.Posts);
    }

    [Fact]
    public async Task SpecialAllHashIsRejectedBeforeEveryMutation()
    {
        using var handler = new EndpointHandler(_ => string.Empty);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.AddTagAsync(["all"], "jfStopped", CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.StopTorrentsAsync(["ALL"], CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.StartTorrentsAsync(["all"], CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.RemoveTagAsync(["all"], "jfStopped", CancellationToken.None)).ConfigureAwait(true);

        Assert.Empty(handler.Posts);
    }

    private static QbittorrentClient CreateClient(HttpClient httpClient)
    {
        return new QbittorrentClient(
            httpClient,
            new QbittorrentConnectionOptions(
                new Uri("http://qbit.internal"),
                TimeSpan.FromSeconds(5)),
            new StoredApiKeyCredentialSource(ApiKey));
    }

    private sealed class EndpointHandler(Func<string, string> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Posts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post)
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Posts.Add(new RecordedRequest(path, body));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(path)),
            };
        }
    }

    private sealed record RecordedRequest(string Path, string Body);
}
