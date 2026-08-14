using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.QBittorrent;

public sealed class QbittorrentVersionTests
{
    private const string ApiKey = "qbt_0123456789abcdefghijklmnopqr";

    [Fact]
    public async Task CompatibleServerReturnsTypedVersions()
    {
        using var handler = new VersionHandler("v5.2.3", "2.15.1");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var server = await client
            .GetServerInfoAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(new Version(5, 2, 3), server.ApplicationVersion);
        Assert.Equal(new Version(2, 15, 1), server.WebApiVersion);
        Assert.Equal(
            ["/api/v2/app/version", "/api/v2/app/webapiVersion"],
            handler.Paths);
    }

    [Theory]
    [InlineData("v5.1.9", "2.15.1")]
    [InlineData("v5.2.0", "2.13.9")]
    [InlineData("v5.2.0", "3.0.0")]
    [InlineData("not-a-version", "2.15.1")]
    [InlineData("v5.2.0", "not-a-version")]
    public async Task UnsupportedOrMalformedVersionsAreRejected(
        string applicationVersion,
        string webApiVersion)
    {
        using var handler = new VersionHandler(applicationVersion, webApiVersion);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
            () => client.GetServerInfoAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(QbittorrentClientError.UnsupportedVersion, exception.Error);
        Assert.DoesNotContain(applicationVersion, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(webApiVersion, exception.Message, StringComparison.Ordinal);
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

    private sealed class VersionHandler(
        string applicationVersion,
        string webApiVersion) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Paths.Add(path);
            var content = path.EndsWith("/webapiVersion", StringComparison.Ordinal)
                ? webApiVersion
                : applicationVersion;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
            });
        }
    }
}
