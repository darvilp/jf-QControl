using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Configuration;

public sealed class QbittorrentConnectionProbeTests
{
    private const string ApiKey = "qbt_1234567890123456789012345678";

    [Fact]
    public async Task ActivationClientUsesJournalEndpointAndLatestConfiguredCredential()
    {
        var configuration = new PluginConfiguration
        {
            QbittorrentBaseAddress = "http://saved-next-host:9090",
            CredentialMode = QbittorrentCredentialMode.StoredApiKey,
            QbittorrentApiKey = ApiKey,
        };
        var persistence = new StaticPersistence(configuration);
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var factory = new ConfiguredQbittorrentClientFactory(
            new SingleHttpClientFactory(httpClient),
            persistence);

        var client = factory.Create(new QbittorrentEndpointIdentity(
            "http",
            "activation-host",
            8080,
            "/webui"));
        var result = await client.GetServerInfoAsync(CancellationToken.None);

        Assert.Equal(new Version(5, 2, 3), result.ApplicationVersion);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("activation-host", request.Uri.Host);
            Assert.StartsWith("/webui/api/v2/", request.Uri.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(ApiKey, request.Authorization);
        });
    }

    [Fact]
    public async Task ProbeReturnsSortedCategoriesAndMapsBoundedFailures()
    {
        var client = new MockQbittorrentClient();
        var factory = new MockClientFactory(client);
        var probe = new QbittorrentConnectionProbe(factory);
        var candidate = new PluginConfiguration();

        var connected = await probe.ProbeAsync(candidate, CancellationToken.None);

        Assert.True(connected.IsConnected);
        Assert.Equal("5.2.3", connected.ApplicationVersion);
        Assert.Equal(["radarr", "sonarr"], connected.Categories);

        client.Failure = new QbittorrentClientException(
            QbittorrentClientError.Authentication,
            "Authentication failed.");
        var failed = await probe.ProbeAsync(candidate, CancellationToken.None);

        Assert.False(failed.IsConnected);
        Assert.Equal(JournalFailureCode.Authentication, failed.Failure);
        Assert.Empty(failed.Categories);
    }

    private sealed class StaticPersistence(PluginConfiguration configuration)
        : IPluginConfigurationPersistence
    {
        public PluginConfiguration Current => configuration;

        public void Save(PluginConfiguration next) => throw new NotSupportedException();
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.Parameter));
            var content = request.RequestUri!.AbsolutePath.EndsWith(
                "/webapiVersion",
                StringComparison.Ordinal)
                ? "2.15.1"
                : "v5.2.3";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
            });
        }
    }

    private sealed class MockClientFactory(IQbittorrentClient client)
        : IQbittorrentClientFactory
    {
        public IQbittorrentClient Create(QbittorrentEndpointIdentity endpoint) => client;

        public IQbittorrentClient Create(PluginConfiguration configuration) => client;
    }

    private sealed class MockQbittorrentClient : IQbittorrentClient
    {
        public QbittorrentClientException? Failure { get; set; }

        public Task<QbittorrentServerInfo> GetServerInfoAsync(CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new QbittorrentServerInfo(
                new Version(5, 2, 3),
                new Version(2, 15, 1)));
        }

        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(["sonarr", "radarr"]);
        }

        public Task<IReadOnlyList<Jellyfin.Plugin.QControl.Domain.Torrents.TorrentSnapshot>>
            GetTorrentsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> GetAlternativeLimitsEnabledAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartTorrentsAsync(
            IEnumerable<string> hashes,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveTagAsync(
            IEnumerable<string> hashes,
            string tag,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetAlternativeLimitsEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
