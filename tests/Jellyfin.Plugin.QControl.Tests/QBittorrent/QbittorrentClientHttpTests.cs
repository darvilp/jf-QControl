using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.QBittorrent;

public sealed class QbittorrentClientHttpTests
{
    private const string ApiKey = "qbt_0123456789abcdefghijklmnopqr";

    [Fact]
    public async Task RequestUsesBearerHeaderAndPreservesConfiguredBasePath()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("v5.2.3"),
            }));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, "https://qbit.example/control/root");

        var version = await client
            .GetApplicationVersionAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal("v5.2.3", version);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://qbit.example/control/root/api/v2/app/version",
            request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(ApiKey, request.AuthorizationParameter);
        Assert.DoesNotContain(ApiKey, request.RequestUri?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestTimeoutProducesBoundedSecretSafeFailure()
    {
        using var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            "http://qbit.internal",
            requestTimeout: TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
            () => client.GetApplicationVersionAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(QbittorrentClientError.Timeout, exception.Error);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellation()
    {
        using var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, "http://qbit.internal");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetApplicationVersionAsync(cancellation.Token)).ConfigureAwait(true);
    }

    [Fact]
    public async Task AdvertisedOversizedResponseIsRejectedBeforeContentUse()
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            var content = new StringContent("v5.2.3");
            content.Headers.ContentLength = 100_000_000;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            });
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, "http://qbit.internal");

        var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
            () => client.GetApplicationVersionAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(QbittorrentClientError.InvalidResponse, exception.Error);
    }

    [Fact]
    public void BaseUrlWithEmbeddedCredentialsIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => new QbittorrentConnectionOptions(
            new Uri("https://user:password@qbit.example"),
            TimeSpan.FromSeconds(5)));

        Assert.DoesNotContain("password", exception.ToString(), StringComparison.Ordinal);
    }

    private static QbittorrentClient CreateClient(
        HttpClient httpClient,
        string baseUrl,
        TimeSpan? requestTimeout = null)
    {
        return new QbittorrentClient(
            httpClient,
            new QbittorrentConnectionOptions(
                new Uri(baseUrl),
                requestTimeout ?? TimeSpan.FromSeconds(5)),
            new StoredApiKeyCredentialSource(ApiKey));
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        public System.Collections.Generic.List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return await responseFactory(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
