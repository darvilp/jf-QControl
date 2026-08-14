using System;
using System.Net.Http;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Builds short-lived clients over host-pooled transports and current credential configuration.
/// </summary>
public sealed class ConfiguredQbittorrentClientFactory : IQbittorrentClientFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginConfigurationPersistence _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredQbittorrentClientFactory"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The host-pooled HTTP transport factory.</param>
    /// <param name="configuration">The current credential source selection.</param>
    public ConfiguredQbittorrentClientFactory(
        IHttpClientFactory httpClientFactory,
        IPluginConfigurationPersistence configuration)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public IQbittorrentClient Create(QbittorrentEndpointIdentity endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var builder = new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.Port)
        {
            Path = endpoint.BasePath,
        };
        return Create(builder.Uri, _configuration.Current);
    }

    /// <inheritdoc />
    public IQbittorrentClient Create(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Create(
            new Uri(configuration.QbittorrentBaseAddress, UriKind.Absolute),
            configuration);
    }

    private QbittorrentClient Create(
        Uri endpoint,
        PluginConfiguration credentialConfiguration)
    {
        IQbittorrentCredentialSource credentialSource =
            credentialConfiguration.CredentialMode switch
            {
                QbittorrentCredentialMode.StoredApiKey =>
                    new StoredApiKeyCredentialSource(credentialConfiguration.QbittorrentApiKey),
                QbittorrentCredentialMode.SecretFile =>
                    new SecretFileCredentialSource(credentialConfiguration.SecretFilePath),
                QbittorrentCredentialMode.Unauthenticated =>
                    new UnauthenticatedCredentialSource(),
                _ => throw new InvalidOperationException("Unknown qBittorrent credential mode."),
            };
        return new QbittorrentClient(
            _httpClientFactory.CreateClient("QControl.qBittorrent"),
            new QbittorrentConnectionOptions(endpoint, RequestTimeout),
            credentialSource);
    }
}
