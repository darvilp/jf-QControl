using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.QBittorrent;

public sealed class QbittorrentRedactionTests
{
    private const string ApiKey = "qbt_0123456789abcdefghijklmnopqr";

    [Fact]
    public async Task AuthenticationResponseBodyIsNeverIncludedInFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(string.Concat("rejected ", ApiKey)),
        };
        using var handler = new FixedHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
            () => client.GetApplicationVersionAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(QbittorrentClientError.Authentication, exception.Error);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedPayloadIsNeverIncludedInFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat("{ malformed ", ApiKey)),
        };
        using var handler = new FixedHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
            () => client.GetTorrentsAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(QbittorrentClientError.InvalidResponse, exception.Error);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransportExceptionMessageAndInnerExceptionAreDropped()
    {
        using var handler = new ThrowingHandler();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
            () => client.GetApplicationVersionAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(QbittorrentClientError.Connection, exception.Error);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    private static QbittorrentClient CreateClient(HttpClient httpClient)
    {
        return new QbittorrentClient(
            httpClient,
            new QbittorrentConnectionOptions(
                new Uri("https://qbit.internal"),
                TimeSpan.FromSeconds(5)),
            new StoredApiKeyCredentialSource(ApiKey));
    }

    private sealed class FixedHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException(
                string.Concat("transport exposed ", ApiKey),
                new InvalidOperationException(ApiKey));
        }
    }
}
