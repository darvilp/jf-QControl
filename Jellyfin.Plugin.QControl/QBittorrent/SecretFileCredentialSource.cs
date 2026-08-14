using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Resolves the current API key from a native Windows, Unix, or container path.
/// </summary>
public sealed class SecretFileCredentialSource : IQbittorrentCredentialSource
{
    private const long MaximumSecretFileBytes = 4096;
    private readonly string _secretFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretFileCredentialSource"/> class.
    /// </summary>
    /// <param name="secretFilePath">The platform-native secret file path.</param>
    public SecretFileCredentialSource(string secretFilePath)
    {
        if (string.IsNullOrWhiteSpace(secretFilePath))
        {
            throw new ArgumentException("A secret-file path is required.", nameof(secretFilePath));
        }

        _secretFilePath = secretFilePath;
    }

    /// <inheritdoc />
    public async ValueTask<QbittorrentApiKey> GetApiKeyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(_secretFilePath);
            if (!file.Exists || file.Length > MaximumSecretFileBytes)
            {
                throw CredentialUnavailable();
            }

            var content = await File.ReadAllTextAsync(_secretFilePath, cancellationToken)
                .ConfigureAwait(false);
            return QbittorrentApiKey.Create(content.TrimEnd('\r', '\n'));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (QbittorrentClientException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            throw CredentialUnavailable();
        }
    }

    private static QbittorrentClientException CredentialUnavailable()
    {
        return new QbittorrentClientException(
            QbittorrentClientError.Credential,
            "The configured qBittorrent secret file is unavailable.");
    }
}
