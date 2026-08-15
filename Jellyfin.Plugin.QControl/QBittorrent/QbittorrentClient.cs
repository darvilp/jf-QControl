using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Narrow, allowlisted qBittorrent 5.2 Web API client.
/// </summary>
public sealed class QbittorrentClient : IQbittorrentClient
{
    private const long MaximumResponseBytes = 64L * 1024L * 1024L;
    private static readonly Version MinimumApplicationVersion = new(5, 2, 0);
    private static readonly Version MinimumWebApiVersion = new(2, 14, 1);

    private readonly HttpClient _httpClient;
    private readonly QbittorrentConnectionOptions _options;
    private readonly IQbittorrentCredentialSource _credentialSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="QbittorrentClient"/> class.
    /// </summary>
    /// <param name="httpClient">The host-managed HTTP transport.</param>
    /// <param name="options">The validated endpoint and deadline.</param>
    /// <param name="credentialSource">The replaceable credential source.</param>
    public QbittorrentClient(
        HttpClient httpClient,
        QbittorrentConnectionOptions options,
        IQbittorrentCredentialSource credentialSource)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialSource);
        _httpClient = httpClient;
        _options = options;
        _credentialSource = credentialSource;
    }

    /// <summary>
    /// Reads the qBittorrent application version.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The non-empty version response.</returns>
    public Task<string> GetApplicationVersionAsync(CancellationToken cancellationToken)
    {
        return GetTextAsync("app/version", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QbittorrentServerInfo> GetServerInfoAsync(
        CancellationToken cancellationToken)
    {
        var rawApplicationVersion = await GetApplicationVersionAsync(cancellationToken)
            .ConfigureAwait(false);
        var rawWebApiVersion = await GetTextAsync("app/webapiVersion", cancellationToken)
            .ConfigureAwait(false);

        if (!TryParseVersion(rawApplicationVersion, trimApplicationPrefix: true, out var applicationVersion)
            || !TryParseVersion(rawWebApiVersion, trimApplicationPrefix: false, out var webApiVersion)
            || applicationVersion < MinimumApplicationVersion
            || webApiVersion < MinimumWebApiVersion
            || webApiVersion.Major != MinimumWebApiVersion.Major)
        {
            throw new QbittorrentClientException(
                QbittorrentClientError.UnsupportedVersion,
                "QControl requires qBittorrent 5.2 or newer with a compatible Web API 2.x version.");
        }

        return new QbittorrentServerInfo(applicationVersion, webApiVersion);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TorrentSnapshot>> GetTorrentsAsync(
        CancellationToken cancellationToken)
    {
        var content = await GetTextAsync("torrents/info", cancellationToken).ConfigureAwait(false);

        try
        {
            var responses = JsonSerializer.Deserialize<TorrentResponse[]>(content);
            if (responses is null)
            {
                throw InvalidResponse();
            }

            var torrents = responses
                .Select(ToTorrentSnapshot)
                .OrderBy(torrent => torrent.Hash, StringComparer.Ordinal)
                .ToArray();
            return Array.AsReadOnly(torrents);
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
        catch (ArgumentException)
        {
            throw InvalidResponse();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCategoriesAsync(
        CancellationToken cancellationToken)
    {
        var content = await GetTextAsync("torrents/categories", cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidResponse();
            }

            var categories = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return Array.AsReadOnly(categories);
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTagsAsync(
        CancellationToken cancellationToken)
    {
        var content = await GetTextAsync("torrents/tags", cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var response = JsonSerializer.Deserialize<string[]>(content);
            if (response is null || response.Any(string.IsNullOrWhiteSpace))
            {
                throw InvalidResponse();
            }

            var tags = response
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return Array.AsReadOnly(tags);
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    /// <inheritdoc />
    public async Task<bool> GetAlternativeLimitsEnabledAsync(
        CancellationToken cancellationToken)
    {
        var content = await GetTextAsync("transfer/speedLimitsMode", cancellationToken)
            .ConfigureAwait(false);
        return content switch
        {
            "0" => false,
            "1" => true,
            _ => throw InvalidResponse(),
        };
    }

    /// <inheritdoc />
    public Task AddTagAsync(
        IEnumerable<string> hashes,
        string tag,
        CancellationToken cancellationToken)
    {
        return ChangeTagAsync("torrents/addTags", hashes, tag, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopTorrentsAsync(
        IEnumerable<string> hashes,
        CancellationToken cancellationToken)
    {
        return ChangeTorrentStateAsync("torrents/stop", hashes, cancellationToken);
    }

    /// <inheritdoc />
    public Task StartTorrentsAsync(
        IEnumerable<string> hashes,
        CancellationToken cancellationToken)
    {
        return ChangeTorrentStateAsync("torrents/start", hashes, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveTagAsync(
        IEnumerable<string> hashes,
        string tag,
        CancellationToken cancellationToken)
    {
        return ChangeTagAsync("torrents/removeTags", hashes, tag, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAlternativeLimitsEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        return PostFormAsync(
            "transfer/setSpeedLimitsMode",
            [new KeyValuePair<string, string>("mode", enabled ? "1" : "0")],
            cancellationToken);
    }

    private static TorrentSnapshot ToTorrentSnapshot(TorrentResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Hash)
            || response.AmountLeft is null
            || response.AmountLeft < 0
            || string.IsNullOrWhiteSpace(response.State))
        {
            throw InvalidResponse();
        }

        var tags = (response.Tags ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new TorrentSnapshot(
            response.Hash,
            string.IsNullOrEmpty(response.Category) ? null : response.Category,
            response.AmountLeft.Value,
            response.State.StartsWith("stopped", StringComparison.Ordinal),
            tags);
    }

    private Task ChangeTorrentStateAsync(
        string endpoint,
        IEnumerable<string> hashes,
        CancellationToken cancellationToken)
    {
        var normalizedHashes = NormalizeHashes(hashes);
        return normalizedHashes.Count == 0
            ? Task.CompletedTask
            : PostFormAsync(
                endpoint,
                [new KeyValuePair<string, string>("hashes", string.Join('|', normalizedHashes))],
                cancellationToken);
    }

    private Task ChangeTagAsync(
        string endpoint,
        IEnumerable<string> hashes,
        string tag,
        CancellationToken cancellationToken)
    {
        ValidateTag(tag);
        var normalizedHashes = NormalizeHashes(hashes);
        return normalizedHashes.Count == 0
            ? Task.CompletedTask
            : PostFormAsync(
                endpoint,
                [
                    new KeyValuePair<string, string>("hashes", string.Join('|', normalizedHashes)),
                    new KeyValuePair<string, string>("tags", tag),
                ],
                cancellationToken);
    }

    private async Task<string> GetTextAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        var content = await SendAsync(
            HttpMethod.Get,
            endpoint,
            form: null,
            cancellationToken).ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw InvalidResponse();
        }

        return content;
    }

    private async Task PostFormAsync(
        string endpoint,
        IReadOnlyList<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        _ = await SendAsync(HttpMethod.Post, endpoint, form, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string endpoint,
        IReadOnlyList<KeyValuePair<string, string>>? form,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);

        try
        {
            var apiKey = await _credentialSource
                .GetApiKeyAsync(deadline.Token)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                method,
                _options.ResolveApiPath(string.Concat("api/v2/", endpoint)));
            if (apiKey is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiKey.RevealForAuthorizationHeader());
            }

            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            EnsureSuccess(response.StatusCode);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                throw InvalidResponse();
            }

            try
            {
                await response.Content
                    .LoadIntoBufferAsync(MaximumResponseBytes, deadline.Token)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                throw InvalidResponse();
            }

            return (await response.Content
                .ReadAsStringAsync(deadline.Token)
                .ConfigureAwait(false)).Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new QbittorrentClientException(
                QbittorrentClientError.Timeout,
                "The qBittorrent request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new QbittorrentClientException(
                QbittorrentClientError.Connection,
                "The qBittorrent endpoint could not be reached.");
        }
    }

    private static ReadOnlyCollection<string> NormalizeHashes(IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        var normalized = hashes
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Any(hash => string.IsNullOrWhiteSpace(hash)
            || string.Equals(hash, "all", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Torrent mutations require explicit non-special hashes.",
                nameof(hashes));
        }

        return Array.AsReadOnly(normalized);
    }

    private static void ValidateTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Contains(',', StringComparison.Ordinal))
        {
            throw new ArgumentException("A mutation requires one non-empty exact tag.", nameof(tag));
        }
    }

    private static void EnsureSuccess(HttpStatusCode statusCode)
    {
        if (statusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
        {
            return;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new QbittorrentClientException(
                QbittorrentClientError.Authentication,
                "qBittorrent rejected the configured authentication mode.");
        }

        throw new QbittorrentClientException(
            QbittorrentClientError.InvalidResponse,
            string.Create(
                CultureInfo.InvariantCulture,
                $"qBittorrent returned HTTP status {(int)statusCode}."));
    }

    private static bool TryParseVersion(
        string value,
        bool trimApplicationPrefix,
        out Version version)
    {
        var candidate = trimApplicationPrefix && value.StartsWith('v')
            ? value[1..]
            : value;
        return Version.TryParse(candidate, out version!);
    }

    private static QbittorrentClientException InvalidResponse()
    {
        return new QbittorrentClientException(
            QbittorrentClientError.InvalidResponse,
            "qBittorrent returned an invalid response.");
    }

    private sealed class TorrentResponse
    {
        [JsonPropertyName("hash")]
        public string? Hash { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }

        [JsonPropertyName("amount_left")]
        public long? AmountLeft { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("tags")]
        public string? Tags { get; init; }
    }
}
