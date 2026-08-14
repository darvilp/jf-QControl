using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Atomically persists and classifies one active activation journal.
/// </summary>
public sealed class ActivationJournalStore : IActivationJournalStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumJournalBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string _journalPath;
    private readonly IActivationJournalFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivationJournalStore"/> class.
    /// </summary>
    /// <param name="journalPath">The resolved final journal path.</param>
    /// <param name="fileSystem">The atomic filesystem port.</param>
    public ActivationJournalStore(
        string journalPath,
        IActivationJournalFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
        {
            throw new ArgumentException("A final journal path is required.", nameof(journalPath));
        }

        ArgumentNullException.ThrowIfNull(fileSystem);
        _journalPath = journalPath;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Writes a complete valid document before atomically replacing the prior journal.
    /// </summary>
    /// <param name="document">The complete new state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing durable replacement.</returns>
    public async ValueTask WriteAsync(
        ActivationJournalDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        var content = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        if (ContainsCredentialMaterial(content))
        {
            throw new ArgumentException(
                "The activation journal contains forbidden credential material.",
                nameof(document));
        }

        if (content.Length > MaximumJournalBytes)
        {
            throw new ArgumentException("The activation journal exceeds its size bound.", nameof(document));
        }

        var temporaryPath = string.Concat(
            _journalPath,
            ".",
            Guid.NewGuid().ToString("N"),
            ".tmp");
        try
        {
            await _fileSystem
                .WriteTemporaryAsync(temporaryPath, content, cancellationToken)
                .ConfigureAwait(false);
            await _fileSystem
                .ReplaceAsync(temporaryPath, _journalPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            throw new ActivationJournalException();
        }
        finally
        {
            try
            {
                await _fileSystem
                    .DeleteIfExistsAsync(temporaryPath, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                // A same-directory orphan cannot expose a partial final journal.
            }
        }
    }

    /// <summary>
    /// Loads a journal and derives conservative automatic authority.
    /// </summary>
    /// <param name="currentProcessInstanceId">The current uninterrupted process identity.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The classified journal state.</returns>
    public async ValueTask<ActivationJournalLoadResult> LoadAsync(
        Guid currentProcessInstanceId,
        CancellationToken cancellationToken)
    {
        if (currentProcessInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A current process identity is required.", nameof(currentProcessInstanceId));
        }

        byte[] content;
        try
        {
            if (!await _fileSystem.ExistsAsync(_journalPath, cancellationToken).ConfigureAwait(false))
            {
                return new ActivationJournalLoadResult(
                    ActivationJournalLoadStatus.Missing,
                    ActivationJournalAuthority.None,
                    null);
            }

            content = await _fileSystem
                .ReadAsync(_journalPath, MaximumJournalBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            throw new ActivationJournalException();
        }

        if (ContainsCredentialMaterial(content)
            || !TryReadSchema(content, out var schemaVersion))
        {
            return Invalid(ActivationJournalLoadStatus.Corrupt);
        }

        if (schemaVersion != CurrentSchemaVersion)
        {
            return Invalid(ActivationJournalLoadStatus.UnsupportedSchema);
        }

        try
        {
            var document = JsonSerializer.Deserialize<ActivationJournalDocument>(
                content,
                SerializerOptions);
            if (document is null)
            {
                return Invalid(ActivationJournalLoadStatus.Corrupt);
            }

            Validate(document);
            return document.ProcessInstanceId == currentProcessInstanceId
                ? new ActivationJournalLoadResult(
                    ActivationJournalLoadStatus.Active,
                    ActivationJournalAuthority.Full,
                    document)
                : new ActivationJournalLoadResult(
                    ActivationJournalLoadStatus.Interrupted,
                    ActivationJournalAuthority.ProtectOnly,
                    document);
        }
        catch (JsonException)
        {
            return Invalid(ActivationJournalLoadStatus.Corrupt);
        }
        catch (ArgumentException)
        {
            return Invalid(ActivationJournalLoadStatus.Corrupt);
        }
    }

    /// <summary>
    /// Deletes the journal after all actions settle or explicit recovery resolution.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task representing deletion completion.</returns>
    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _fileSystem
                .DeleteIfExistsAsync(_journalPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            throw new ActivationJournalException();
        }
    }

    private static void Validate(ActivationJournalDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.ProcessInstanceId == Guid.Empty
            || document.ActivationId == Guid.Empty
            || document.StartedAt == default
            || document.SessionIds.IsDefault
            || document.SessionIds.Length == 0
            || document.SessionIds.Any(string.IsNullOrWhiteSpace)
            || document.SessionIds.Distinct(StringComparer.Ordinal).Count() != document.SessionIds.Length
            || document.Configuration is null
            || document.Configuration.SelectedCategories.IsDefault
            || document.Endpoint is null
            || document.AlternativeLimits is null
            || document.Torrents.IsDefault
            || document.Torrents.Any(entry => entry is null)
            || document.Phase == ProtectionPhase.Inactive
            || !Enum.IsDefined(document.Phase)
            || (document.LastFailure.HasValue && !Enum.IsDefined(document.LastFailure.Value)))
        {
            throw new ArgumentException("The activation journal violates required invariants.", nameof(document));
        }

        if (document.Configuration.ReleaseGrace < TimeSpan.Zero
            || document.Configuration.Revision < 0
            || !Enum.IsDefined(document.Configuration.StopScope)
            || (!document.Configuration.AlternativeLimitsEnabled
                && !document.Configuration.StopTorrentsEnabled))
        {
            throw new ArgumentException("The activation configuration snapshot is invalid.", nameof(document));
        }

        if (!IsOrdinallySortedUnique(document.SessionIds)
            || !IsOrdinallySortedUnique(document.Configuration.SelectedCategories))
        {
            throw new ArgumentException(
                "Journal identifier and category collections must be deterministic.",
                nameof(document));
        }

        if (document.Configuration.StopTorrentsEnabled)
        {
            _ = new TorrentSelectionPolicy(
                document.Configuration.StopScope,
                document.Configuration.SelectedCategories,
                document.Configuration.IncludeIncomplete,
                document.Configuration.IncludeCompleted,
                document.Configuration.MarkerTag,
                document.Configuration.NeverTouchTag);
        }
        else if (document.Torrents.Length > 0)
        {
            throw new ArgumentException(
                "Torrent progress cannot exist when the stop action is disabled.",
                nameof(document));
        }

        ValidateAlternativeLimits(document);

        if ((document.Phase == ProtectionPhase.ReleasePending) != document.ReleaseDueAt.HasValue)
        {
            throw new ArgumentException("Release timing does not match the journal phase.", nameof(document));
        }

        if (!string.Equals(document.Endpoint.Scheme, "http", StringComparison.Ordinal)
            && !string.Equals(document.Endpoint.Scheme, "https", StringComparison.Ordinal))
        {
            throw new ArgumentException("The endpoint identity scheme is invalid.", nameof(document));
        }

        if (string.IsNullOrWhiteSpace(document.Endpoint.Host)
            || document.Endpoint.Port is <= 0 or > 65535
            || string.IsNullOrEmpty(document.Endpoint.BasePath)
            || !document.Endpoint.BasePath.StartsWith('/'))
        {
            throw new ArgumentException("The endpoint identity is invalid.", nameof(document));
        }

        var torrentHashes = document.Torrents.Select(entry => entry.Hash).ToImmutableArray();
        if (!IsOrdinallySortedUnique(torrentHashes)
            || document.Torrents.Any(entry => string.IsNullOrWhiteSpace(entry.Hash)
                || string.Equals(entry.Hash, "all", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Torrent journal entries require unique explicit hashes.", nameof(document));
        }

        foreach (var entry in document.Torrents)
        {
            if (!Enum.IsDefined(entry.MarkerAddStage)
                || !Enum.IsDefined(entry.StopStage)
                || !Enum.IsDefined(entry.StartStage)
                || !Enum.IsDefined(entry.MarkerRemoveStage)
                || (entry.StopStage != JournalMutationStage.None
                    && entry.MarkerAddStage != JournalMutationStage.Confirmed)
                || (entry.MarkerRemoveStage != JournalMutationStage.None
                    && entry.StartStage != JournalMutationStage.Confirmed))
            {
                throw new ArgumentException(
                    "Torrent mutation progress violates durable operation ordering.",
                    nameof(document));
            }
        }

        if (document.ReleaseDueAt < document.StartedAt
            || document.LastSuccessfulReconciliation < document.StartedAt)
        {
            throw new ArgumentException("Journal timestamps precede activation start.", nameof(document));
        }
    }

    private static void ValidateAlternativeLimits(ActivationJournalDocument document)
    {
        var state = document.AlternativeLimits;
        if (!Enum.IsDefined(state.EnableStage) || !Enum.IsDefined(state.DisableStage))
        {
            throw new ArgumentException("Alternative Limits progress is undefined.", nameof(document));
        }

        if (!document.Configuration.AlternativeLimitsEnabled)
        {
            if (state.InitialEnabled.HasValue
                || state.EnabledByActivation
                || state.EnableStage != JournalMutationStage.None
                || state.DisableStage != JournalMutationStage.None)
            {
                throw new ArgumentException(
                    "Alternative Limits progress exists for a disabled action.",
                    nameof(document));
            }

            return;
        }

        if (!state.InitialEnabled.HasValue)
        {
            if (state.EnabledByActivation
                || state.EnableStage != JournalMutationStage.None
                || state.DisableStage != JournalMutationStage.None)
            {
                throw new ArgumentException(
                    "Unobserved Alternative Limits cannot carry mutation progress.",
                    nameof(document));
            }

            return;
        }

        if (state.InitialEnabled.Value && state.EnabledByActivation)
        {
            throw new ArgumentException(
                "An initially enabled Alternative Limits mode cannot be activation-owned.",
                nameof(document));
        }

        if (!state.InitialEnabled.Value
            && ((!state.EnabledByActivation
                    && state.EnableStage == JournalMutationStage.Confirmed)
                || (state.EnabledByActivation
                    && state.EnableStage == JournalMutationStage.None)))
        {
            throw new ArgumentException(
                "Alternative Limits ownership and enable progress are inconsistent.",
                nameof(document));
        }

        if (state.DisableStage != JournalMutationStage.None && !state.EnabledByActivation)
        {
            throw new ArgumentException(
                "Only an owned Alternative Limits transition can be disabled.",
                nameof(document));
        }
    }

    private static bool TryReadSchema(ReadOnlyMemory<byte> content, out int schemaVersion)
    {
        schemaVersion = default;
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("schemaVersion", out var schema)
                && schema.TryGetInt32(out schemaVersion);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ActivationJournalLoadResult Invalid(ActivationJournalLoadStatus status)
    {
        return new ActivationJournalLoadResult(status, ActivationJournalAuthority.None, null);
    }

    private static bool IsPersistenceFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }

    private static bool ContainsCredentialMaterial(ReadOnlySpan<byte> content)
    {
        return content.IndexOf("qbt_"u8) >= 0;
    }

    private static bool IsOrdinallySortedUnique(ImmutableArray<string> values)
    {
        return values.SequenceEqual(
            values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
