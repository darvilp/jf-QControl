using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.QBittorrent;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.QBittorrent;

public sealed class QbittorrentCredentialTests
{
    private const string FirstKey = "qbt_0123456789abcdefghijklmnopqr";
    private const string SecondKey = "qbt_ABCDEFGHIJKLMNOPQRSTUVWXYZ12";

    [Fact]
    public async Task SecretFileUsesPlatformPathAndObservesReplacementPerRequest()
    {
        var secretPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(secretPath, string.Concat(FirstKey, "\r\n"))
                .ConfigureAwait(true);
            using var handler = new AuthorizationHandler();
            using var httpClient = new HttpClient(handler);
            var client = CreateClient(httpClient, new SecretFileCredentialSource(secretPath));

            _ = await client.GetApplicationVersionAsync(CancellationToken.None).ConfigureAwait(true);
            await File.WriteAllTextAsync(secretPath, string.Concat(SecondKey, "\n"))
                .ConfigureAwait(true);
            _ = await client.GetApplicationVersionAsync(CancellationToken.None).ConfigureAwait(true);

            Assert.Equal([FirstKey, SecondKey], handler.AuthorizationParameters);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Fact]
    public async Task InvalidSecretFileContentProducesNoObservableSecret()
    {
        const string secretContent = "not-a-valid-key-SECRET-CONTENT";
        var secretPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(secretPath, secretContent).ConfigureAwait(true);
            using var handler = new AuthorizationHandler();
            using var httpClient = new HttpClient(handler);
            var client = CreateClient(httpClient, new SecretFileCredentialSource(secretPath));

            var exception = await Assert.ThrowsAsync<QbittorrentClientException>(
                () => client.GetApplicationVersionAsync(CancellationToken.None)).ConfigureAwait(true);

            Assert.Equal(QbittorrentClientError.Credential, exception.Error);
            Assert.DoesNotContain(secretContent, exception.ToString(), StringComparison.Ordinal);
            Assert.Empty(handler.AuthorizationParameters);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Fact]
    public void OpaqueStoredCredentialNeverFormatsAsItsSecret()
    {
        var credential = QbittorrentApiKey.Create(FirstKey);

        Assert.Equal("[REDACTED]", credential.ToString());
        Assert.DoesNotContain(FirstKey, credential.ToString(), StringComparison.Ordinal);
    }

    private static QbittorrentClient CreateClient(
        HttpClient httpClient,
        IQbittorrentCredentialSource credentialSource)
    {
        return new QbittorrentClient(
            httpClient,
            new QbittorrentConnectionOptions(
                new Uri("http://qbit.internal"),
                TimeSpan.FromSeconds(5)),
            credentialSource);
    }

    private sealed class AuthorizationHandler : HttpMessageHandler
    {
        public List<string?> AuthorizationParameters { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("v5.2.3"),
            });
        }
    }
}
