using System;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Validated qBittorrent endpoint and request deadline.
/// </summary>
public sealed class QbittorrentConnectionOptions
{
    private static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="QbittorrentConnectionOptions"/> class.
    /// </summary>
    /// <param name="baseAddress">The HTTP or HTTPS Web UI base address.</param>
    /// <param name="requestTimeout">The complete credential-and-request deadline.</param>
    public QbittorrentConnectionOptions(Uri baseAddress, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        BaseAddress = baseAddress;
        RequestTimeout = requestTimeout;
        Validate();
    }

    /// <summary>
    /// Gets the validated HTTP or HTTPS Web UI base address.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// Gets the complete credential-and-request deadline.
    /// </summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>
    /// Resolves one allowlisted API path beneath the configured base path.
    /// </summary>
    /// <param name="apiPath">A relative path beginning beneath <c>api/v2</c>.</param>
    /// <returns>The absolute request URI.</returns>
    internal Uri ResolveApiPath(string apiPath)
    {
        var builder = new UriBuilder(BaseAddress)
        {
            Path = string.Concat(BaseAddress.AbsolutePath.TrimEnd('/'), "/"),
        };
        return new Uri(builder.Uri, apiPath);
    }

    private void Validate()
    {
        if (!BaseAddress.IsAbsoluteUri
            || (BaseAddress.Scheme != Uri.UriSchemeHttp
                && BaseAddress.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(BaseAddress.Host))
        {
            throw new ArgumentException("qBittorrent requires an absolute HTTP or HTTPS base URL.");
        }

        if (!string.IsNullOrEmpty(BaseAddress.UserInfo))
        {
            throw new ArgumentException("Embedded credentials are not allowed in the qBittorrent base URL.");
        }

        if (!string.IsNullOrEmpty(BaseAddress.Query) || !string.IsNullOrEmpty(BaseAddress.Fragment))
        {
            throw new ArgumentException("The qBittorrent base URL cannot contain a query or fragment.");
        }

        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > MaximumRequestTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeout),
                "The qBittorrent request timeout must be positive and no longer than five minutes.");
        }
    }
}
