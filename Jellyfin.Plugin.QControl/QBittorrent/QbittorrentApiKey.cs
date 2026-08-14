using System;
using System.Linq;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// An opaque validated qBittorrent API key.
/// </summary>
public sealed class QbittorrentApiKey
{
    private readonly string _value;

    private QbittorrentApiKey(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Creates an opaque validated key without echoing invalid content.
    /// </summary>
    /// <param name="value">The configured API-key content.</param>
    /// <returns>The opaque credential.</returns>
    /// <exception cref="QbittorrentClientException">The content is not a qBittorrent API key.</exception>
    public static QbittorrentApiKey Create(string value)
    {
        if (value is null
            || value.Length != 32
            || !value.StartsWith("qbt_", StringComparison.Ordinal)
            || !value.AsSpan(4).ToArray().All(IsAsciiAlphaNumeric))
        {
            throw new QbittorrentClientException(
                QbittorrentClientError.Credential,
                "The configured qBittorrent API key has an invalid format.");
        }

        return new QbittorrentApiKey(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return "[REDACTED]";
    }

    /// <summary>
    /// Gets the credential only for the outbound authorization header.
    /// </summary>
    /// <returns>The validated secret.</returns>
    internal string RevealForAuthorizationHeader()
    {
        return _value;
    }

    private static bool IsAsciiAlphaNumeric(char value)
    {
        return (value >= '0' && value <= '9')
            || (value >= 'A' && value <= 'Z')
            || (value >= 'a' && value <= 'z');
    }
}
